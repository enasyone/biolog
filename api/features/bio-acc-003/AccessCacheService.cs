using System.Security.Cryptography;
using System.Text;
using Biolog.Features.AccessCache.Api;
using Biolog.Features.AccessCache.Audit;
using Biolog.Features.AccessCache.Domain;
using Biolog.Features.AccessCache.Identifiers;

namespace Biolog.Features.AccessCache;

/// <summary>
/// Application service for BIO-ACC-003's server side: AC01/AC02 (the revocation-delta feed an
/// edge device pulls to keep its own local deny-list minimum-necessary and up to date), AC05
/// (idempotent presentation-event intake - the server-side half of "reconnect sync uploads
/// events exactly once," complementing the mobile client's own single-flight upload guard).
/// AC03/AC04 (offline deny + stale-cache policy) are entirely the mobile client's own concern
/// (<c>mobile/lib/core/access/</c>) - by the time a presentation event reaches this service, the
/// deny/allow decision has already been made, offline, against the device's own local cache;
/// this service only durably records it.
/// </summary>
public sealed class AccessCacheService(
    IIdentityRevocationFeedGateway revocationFeedGateway,
    IIdempotencyStore idempotencyStore,
    IPresentationEventLog presentationEventLog,
    IAccessCacheAuditSink auditSink,
    TimeSpan? idempotencyWaitTimeout = null, TimeSpan? idempotencyWaitPollInterval = null)
{
    private const string IntakeOperation = "presentation_event_intake";
    private static readonly string[] ValidOutcomes = ["ALLOWED", "DENIED"];

    // Idempotency-claim-race remediation: how long a claim-loser (a concurrent caller whose
    // Idempotency-Key is already owned by someone else's in-flight attempt) waits for that
    // owner's result before giving up with a 503. Both are injectable (constructor parameters,
    // defaulting to production-sized values) so a test can prove the timeout path
    // deterministically and fast, without actually waiting multiple real seconds.
    private readonly TimeSpan _idempotencyWaitTimeout = idempotencyWaitTimeout ?? TimeSpan.FromSeconds(5);
    private readonly TimeSpan _idempotencyWaitPollInterval = idempotencyWaitPollInterval ?? TimeSpan.FromMilliseconds(20);

    public async Task<RevocationDeltaResponse> GetRevocationDeltaAsync(ResolvedDevice device, string? since, CancellationToken cancellationToken)
    {
        var entries = await revocationFeedGateway.ListRevokedSinceAsync(device.TenantId, since, cancellationToken);
        var cursor = entries.Count > 0 ? entries[^1].RevokedAt : since;
        return new RevocationDeltaResponse { Items = [.. entries.Select(RevocationEntryResponse.From)], Cursor = cursor };
    }

    /// <summary>
    /// Mirrors <c>OfflineMutationIntakeService.IntakeAsync</c>'s audit-before-commit discipline
    /// (ADR-0003 Decision 7): the idempotency claim happens first (so a concurrent duplicate
    /// submission is caught immediately), then the audit write is confirmed durable BEFORE the
    /// event is appended to <see cref="IPresentationEventLog"/> - the real point of no return.
    /// Unlike BIO-MOB-003's intake there is no aggregate-version ledger here: a presentation
    /// event is always a fresh append, never a CAS-guarded update, so there is nothing to peek
    /// or conflict against beyond the idempotency key itself.
    /// </summary>
    public async Task<(PresentationEventAckResponse Result, bool Replayed)> IntakePresentationEventAsync(
        ResolvedDevice device, PresentationEventIntakeRequest request, string idempotencyKey,
        AuditActor actor, string correlationId, CancellationToken cancellationToken)
    {
        Validate(request);

        if (request.TenantId != device.TenantId)
        {
            throw AccessCacheError.TenantMismatch(request.TenantId, device.TenantId);
        }

        if (request.DeviceId != device.DeviceId)
        {
            throw AccessCacheError.DeviceMismatch(request.DeviceId, device.DeviceId);
        }

        var fingerprint = ComputeFingerprint(request);

        // Idempotency-claim-race remediation (Tunde/Seyi/Zainab's finding, Kemi-confirmed High
        // severity): the original code checked only `claimed.State == Completed` - a shared
        // InFlight record from a CONCURRENT caller looked identical to a claim this caller itself
        // just won, so a second caller fell through and executed a second time, silently
        // replacing the first caller's durable result. `IdempotencyClaim.IsNewClaim` is the
        // missing ownership signal: only the true owner ever breaks out of this loop to execute
        // below. Every other caller (someone else's claim is in flight) waits, bounded by
        // `_idempotencyWaitTimeout`, re-attempting its own `Claim` each iteration.
        var deadline = DateTime.UtcNow + _idempotencyWaitTimeout;
        IdempotencyClaim claim;
        while (true)
        {
            claim = idempotencyStore.Claim(device.TenantId, IntakeOperation, idempotencyKey, fingerprint);
            if (claim.Record.RequestFingerprint != fingerprint)
            {
                throw AccessCacheError.IdempotencyKeyReuse();
            }

            if (claim.Record.State == IdempotencyRecordState.Completed)
            {
                return ((PresentationEventAckResponse)claim.Record.Result!, Replayed: true);
            }

            if (claim.IsNewClaim)
            {
                break;
            }

            if (DateTime.UtcNow >= deadline)
            {
                throw AccessCacheError.IdempotencyWaitTimeout(idempotencyKey);
            }

            await Task.Delay(_idempotencyWaitPollInterval, cancellationToken);
        }

        PresentationOutcomeExtensions.TryParse(request.Outcome, out var outcome);
        var receivedAt = Identifiers.UtcInstant.Now();
        var presentationEvent = new PresentationEvent(
            request.EventId, device.TenantId, device.DeviceId, request.PersonId, outcome, request.ReasonCode,
            request.OccurredAt, receivedAt, request.CacheSnapshotAt, request.Stale);

        try
        {
            await AuditAsync(device.TenantId, actor, presentationEvent, correlationId, cancellationToken);
        }
        catch
        {
            idempotencyStore.Release(device.TenantId, IntakeOperation, idempotencyKey);
            throw;
        }

        // Audit write confirmed durable - the real point of no return (same discipline as
        // OfflineMutationIntakeService/CardIssueService). CancellationToken.None: a caller
        // cancellation arriving after this point must not abort an append whose audit event
        // already durably claims it happened.
        await presentationEventLog.AppendAsync(presentationEvent, CancellationToken.None);
        var result = PresentationEventAckResponse.From(presentationEvent);
        idempotencyStore.Complete(device.TenantId, IntakeOperation, idempotencyKey, result);
        return (result, Replayed: false);
    }

    public async Task<PresentationEventListResponse> ListRecentPresentationEventsAsync(string tenantId, int limit, CancellationToken cancellationToken)
    {
        var events = await presentationEventLog.ListRecentAsync(tenantId, limit, cancellationToken);
        return new PresentationEventListResponse { Items = [.. events.Select(PresentationEventResponse.From)] };
    }

    private static void Validate(PresentationEventIntakeRequest request)
    {
        List<ValidationErrorItem>? errors = null;
        void Add(string field, string code, string message) => (errors ??= []).Add(new ValidationErrorItem(field, code, message));

        if (!CanonicalId.IsEventId(request.EventId)) Add("event_id", "invalid_format", "must be a canonical evt_ identifier");
        if (!CanonicalId.IsTenantId(request.TenantId)) Add("tenant_id", "invalid_format", "must be a canonical ten_<uuidv7> id");
        if (!CanonicalId.IsDeviceId(request.DeviceId)) Add("device_id", "invalid_format", "must be a canonical dev_<uuidv7> id");
        if (!CanonicalId.IsPersonId(request.PersonId)) Add("person_id", "invalid_format", "must be a canonical per_<uuidv7> id");
        if (!Array.Exists(ValidOutcomes, o => o == request.Outcome)) Add("outcome", "invalid_value", "must be one of ALLOWED, DENIED");
        if (string.IsNullOrWhiteSpace(request.ReasonCode)) Add("reason_code", "required", "reason_code is required");
        if (string.IsNullOrWhiteSpace(request.OccurredAt)) Add("occurred_at", "required", "occurred_at is required");

        if (errors is not null)
        {
            throw AccessCacheError.Validation(errors);
        }
    }

    /// <summary>Every identity-bearing claim (tenant/device/person/outcome/content) is bound into the fingerprint, not only the event id - mirrors BIO-MOB-003-QA-02's defense-in-depth reasoning: a replayed idempotency key with different content is a fingerprint mismatch (409), never silently treated as an identical replay.</summary>
    internal static string ComputeFingerprint(PresentationEventIntakeRequest request)
    {
        var canonical = $"{request.EventId}|{request.TenantId}|{request.DeviceId}|{request.PersonId}|{request.Outcome}|{request.ReasonCode}|{request.OccurredAt}|{request.CacheSnapshotAt}|{request.Stale}";
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private async Task AuditAsync(string tenantId, AuditActor actor, PresentationEvent presentationEvent, string correlationId, CancellationToken cancellationToken)
    {
        var auditEvent = new AccessCacheAuditEvent(
            CanonicalId.NewEventId(), tenantId, actor, "access_cache.presentation_event.intake",
            new AuditTarget("presentation_event", presentationEvent.EventId),
            presentationEvent.Outcome == PresentationOutcome.Denied ? AuditOutcome.Denied : AuditOutcome.Allowed,
            presentationEvent.Outcome == PresentationOutcome.Denied ? "revoked_cached" : "not_revoked",
            correlationId, DateTimeOffset.UtcNow);

        try
        {
            await auditSink.WriteAsync(auditEvent, cancellationToken);
            return;
        }
        catch (Exception)
        {
            // Ambiguous - fall through to verify rather than assume failure.
        }

        bool exists;
        try
        {
            exists = await auditSink.ExistsAsync(auditEvent.EventId, cancellationToken);
        }
        catch (Exception)
        {
            throw AccessCacheError.AuditUnavailable();
        }

        if (!exists)
        {
            throw AccessCacheError.AuditUnavailable();
        }
    }
}

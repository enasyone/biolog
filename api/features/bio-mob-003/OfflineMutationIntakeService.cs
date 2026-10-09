using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Biolog.Features.OfflineSync.Api;
using Biolog.Features.OfflineSync.Audit;
using Biolog.Features.OfflineSync.Domain;
using Biolog.Features.OfflineSync.Identifiers;

namespace Biolog.Features.OfflineSync;

/// <summary>
/// Application service for BIO-MOB-003's central intake endpoint. Applies ADR-0003 Decision 7's
/// audit-before-commit pattern (established across every prior story in this repository, most
/// directly <c>CardIssueService.IssueAsync</c>, BIO-CRD-005-QA-01): the version ledger's commit
/// is deferred until AFTER the audit write is confirmed durable, not the other way around. A
/// per-(tenant, aggregate) lock (<see cref="OfflineMutationLockRegistry"/>) is held across the
/// whole peek -> audit -> commit sequence, so nothing about the ledger's state changes - and
/// nothing needs to be rolled back - if the audit write fails (BIO-MOB-003 round 4, Seyi: an
/// earlier version committed the ledger BEFORE the audit and could not undo it on audit
/// failure, permanently false-conflicting every retry of a mutation that was never actually
/// accepted). An ambiguous audit-write throw is still resolved via
/// <see cref="IOfflineSyncAuditSink.ExistsAsync"/> rather than assumed failed.
///
/// AC01 (delta sync: additions/updates/revocations), AC02 (idempotent acknowledgements),
/// AC03/AC04 (conflicts unconditionally rejected, never merged - there is no merge branch
/// anywhere below), AC05 (this endpoint's own idempotency guarantee is what makes a client-side
/// reconnect-storm retry safe on the SERVER side too, complementing BIO-MOB-003's client-side
/// single-flight guard).
/// </summary>
internal sealed class OfflineMutationIntakeService(
    IIdempotencyStore idempotencyStore,
    IAggregateVersionLedger versionLedger,
    IOfflineSyncAuditSink auditSink,
    OfflineMutationLockRegistry lockRegistry,
    TimeSpan? idempotencyWaitTimeout = null, TimeSpan? idempotencyWaitPollInterval = null)
{
    private const string IntakeOperation = "offline_mutation_intake";
    private static readonly string[] ValidOperations = ["ADDITION", "UPDATE", "REVOCATION"];

    // Idempotency-claim-race remediation: how long a claim-loser (a concurrent caller whose
    // Idempotency-Key is already owned by someone else's in-flight attempt) waits for that
    // owner's result before giving up with a 503. Both are injectable (constructor parameters,
    // defaulting to production-sized values) so a test can prove the timeout path
    // deterministically and fast, without actually waiting multiple real seconds.
    private readonly TimeSpan _idempotencyWaitTimeout = idempotencyWaitTimeout ?? TimeSpan.FromSeconds(5);
    private readonly TimeSpan _idempotencyWaitPollInterval = idempotencyWaitPollInterval ?? TimeSpan.FromMilliseconds(20);

    public async Task<(OfflineMutationAckResult Result, bool Replayed)> IntakeAsync(
        ResolvedDevice device, OfflineMutationDeltaRequest request, string idempotencyKey,
        AuditActor actor, string correlationId, CancellationToken cancellationToken)
    {
        Validate(request);

        // Kemi's ruling: the payload's tenant_id is a claim, never trusted for routing on its
        // own (ADR-0002 Decision 1.1) - it must match the tenant independently resolved from
        // the authenticated device.
        if (request.TenantId != device.TenantId)
        {
            throw OfflineSyncError.TenantMismatch(request.TenantId, device.TenantId);
        }

        // BIO-MOB-003-QA-02 (Chioma, acceptance round 2): the same discipline, applied to the
        // payload's device_id - a body claim is never trusted on its own. Without this check an
        // authenticated device could submit another device's canonical id as the mutation's
        // claimed origin; audit attribution would still show the REAL caller, but the accepted
        // envelope itself would carry a contradictory, unvalidated provenance claim.
        if (request.DeviceId != device.DeviceId)
        {
            throw OfflineSyncError.DeviceMismatch(request.DeviceId, device.DeviceId);
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
                throw OfflineSyncError.IdempotencyKeyReuse();
            }

            if (claim.Record.State == IdempotencyRecordState.Completed)
            {
                return ((OfflineMutationAckResult)claim.Record.Result!, Replayed: true);
            }

            if (claim.IsNewClaim)
            {
                break;
            }

            if (DateTime.UtcNow >= deadline)
            {
                throw OfflineSyncError.IdempotencyWaitTimeout(idempotencyKey);
            }

            await Task.Delay(_idempotencyWaitPollInterval, cancellationToken);
        }

        // Held across the whole peek -> audit -> commit sequence below: nothing about this
        // aggregate's version ledger changes until the audit write is confirmed durable, so a
        // failed audit leaves nothing to roll back (see this class's own header, BIO-MOB-003
        // round 4).
        using var _ = await lockRegistry.AcquireAsync(device.TenantId, request.AggregateId, cancellationToken);

        var current = versionLedger.Peek(device.TenantId, request.AggregateId);
        if (current is not null && current != request.BaseVersion)
        {
            // AC03/AC04: a version conflict commits nothing - no merge, no silent
            // last-write-wins. Released rather than completed: nothing durable happened for
            // this specific attempt, so a legitimate retry of the identical request (e.g. the
            // server itself retried transparently within ADR-0004's own retry semantics) is not
            // permanently pinned to a stale conflict outcome.
            idempotencyStore.Release(device.TenantId, IntakeOperation, idempotencyKey);
            throw OfflineSyncError.ConcurrencyConflict(request.AggregateId, request.BaseVersion, current.Value);
        }

        // Either the first mutation this ledger has ever seen for this aggregate (current is
        // null - accepted unconditionally per IAggregateVersionLedger's own documented
        // first-mutation rule, adopting base_version as-is), or the caller's claimed
        // base_version matched exactly. Nothing has been committed yet either way.
        var newVersion = current is null ? request.BaseVersion : request.BaseVersion + 1;

        try
        {
            await AuditAsync(device.TenantId, actor, request, newVersion, correlationId, cancellationToken);
        }
        catch
        {
            // Nothing durable was committed - the ledger is untouched, so a retry of this exact
            // mutation is not falsely conflicted against a version that was never really reached.
            idempotencyStore.Release(device.TenantId, IntakeOperation, idempotencyKey);
            throw;
        }

        // The audit write is confirmed durable - this is the real point of no return, the same
        // discipline CardIssueService.IssueAsync already established (BIO-CRD-005-QA-01).
        versionLedger.Commit(device.TenantId, request.AggregateId, newVersion);
        var result = new OfflineMutationAckResult(request.MutationId, request.AggregateId, newVersion, request.Operation);
        idempotencyStore.Complete(device.TenantId, IntakeOperation, idempotencyKey, result);
        return (result, Replayed: false);
    }

    private static void Validate(OfflineMutationDeltaRequest request)
    {
        List<ValidationErrorItem>? errors = null;
        void Add(string field, string code, string message) => (errors ??= []).Add(new ValidationErrorItem(field, code, message));

        if (!CanonicalId.IsMutationId(request.MutationId)) Add("mutation_id", "invalid_format", "must be a canonical identifier");
        if (!CanonicalId.IsTenantId(request.TenantId)) Add("tenant_id", "invalid_format", "must be a canonical ten_<uuidv7> id");
        if (!CanonicalId.IsDeviceId(request.DeviceId)) Add("device_id", "invalid_format", "must be a canonical dev_<uuidv7> id");
        if (!CanonicalId.IsAggregateId(request.AggregateId)) Add("aggregate_id", "invalid_format", "must be a canonical identifier");
        if (request.BaseVersion < 1) Add("base_version", "invalid_value", "must be >= 1");
        if (string.IsNullOrEmpty(request.IdempotencyKey) || request.IdempotencyKey.Length > 255) Add("idempotency_key", "invalid_length", "must be 1-255 characters");
        if (!Array.Exists(ValidOperations, op => op == request.Operation)) Add("operation", "invalid_value", "must be one of ADDITION, UPDATE, REVOCATION");

        if (errors is not null)
        {
            throw OfflineSyncError.Validation(errors);
        }
    }

    /// <summary>
    /// BIO-MOB-003-QA-02 (Chioma, acceptance round 2): every identity-bearing claim
    /// (<c>tenant_id</c>, <c>device_id</c>) is bound into the fingerprint, not only the
    /// mutation's own content fields. The explicit cross-validation checks above already reject
    /// a mismatched claim before this point is ever reached in the CURRENT call order, so this
    /// is defense-in-depth, not the only thing standing between a forged claim and acceptance:
    /// if those checks were ever reordered or one were removed, a request reusing the same
    /// <c>idempotency_key</c> with a changed tenant/device claim would still be caught here, as
    /// a fingerprint mismatch (<c>409 idempotency_key_reuse</c>), rather than silently treated
    /// as an identical replay of the original result. <c>internal</c> (not <c>private</c>) so
    /// the test project can assert directly that changing either claim changes the fingerprint.
    /// </summary>
    internal static string ComputeFingerprint(OfflineMutationDeltaRequest request)
    {
        var canonical = $"{request.MutationId}|{request.TenantId}|{request.DeviceId}|{request.AggregateId}|{request.BaseVersion}|{request.Operation}|{request.Payload.GetRawText()}";
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private async Task AuditAsync(
        string tenantId, AuditActor actor, OfflineMutationDeltaRequest request, int newVersion, string correlationId, CancellationToken cancellationToken)
    {
        var auditEvent = new OfflineSyncAuditEvent(
            CanonicalId.NewEventId(), tenantId, actor, "offline_sync.mutation.intake",
            new AuditTarget("offline_mutation", request.AggregateId), AuditOutcome.Allowed, "mutation_accepted",
            correlationId, DateTimeOffset.UtcNow);

        try
        {
            await auditSink.WriteAsync(auditEvent, cancellationToken);
            return; // confirmed: the write completed without throwing
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
            throw OfflineSyncError.AuditUnavailable(); // total outage: cannot even verify
        }

        if (!exists)
        {
            throw OfflineSyncError.AuditUnavailable(); // confirmed: genuinely never persisted
        }

        // else: confirmed durably present despite the throw on WriteAsync - proceed.
    }
}

using System.Security.Cryptography;
using System.Text;
using Biolog.Features.PhotoBooth.Api;
using Biolog.Features.PhotoBooth.Audit;
using Biolog.Features.PhotoBooth.Domain;
using Biolog.Features.PhotoBooth.Identifiers;
using Biolog.Features.PhotoBooth.Tenancy;

namespace Biolog.Features.PhotoBooth;

/// <summary>
/// Application service for BIO-PHO-002. Implements ADR-0003 Decision 7's audit-atomicity
/// pattern - established across every prior story in this repository - applied fresh here:
/// every mutating method holds an in-process, per-person-id lock
/// (<see cref="BoothCaptureLockRegistry"/>) across its whole read-validate-audit-commit
/// sequence, audits BEFORE committing, resolves an ambiguous audit-write throw via
/// <see cref="ICaptureAuditSink.ExistsAsync"/> rather than assuming failure, and passes
/// <see cref="CancellationToken.None"/> to the repository commit once the audit write is
/// confirmed durable.
///
/// <b>Registration lifetime and locking.</b> Registered Scoped (reads a per-request
/// <see cref="ICurrentTenantProvider"/>) - see <see cref="BoothCaptureLockRegistry"/>'s header.
///
/// <b>AC03 (idempotent retries).</b> <see cref="SubmitAsync"/> is the only retry-prone write
/// (a booth may legitimately retry a network-interrupted upload) - claimed/completed/released
/// against <see cref="IIdempotencyStore"/> exactly per ADR-0001 §3.4's table. A rejection
/// (low-quality capture) is itself a COMPLETED, replayable outcome, not a failure requiring
/// release - replaying the same submission returns the same rejected record, never re-evaluates
/// or re-rejects it a second time with new reasons. <see cref="ApproveAsync"/>/
/// <see cref="RejectAsync"/> are decisions, not retry-prone uploads, and use ADR-0001 §3.2's own
/// compare-and-set (<c>expected_version</c>) instead - the same idempotent-in-effect shape
/// every sibling story's own status transition uses (a replayed approve with a now-stale
/// expected_version correctly reports `concurrency_conflict`, not a silent double-promotion).
///
/// <b>AC04's approval-time disclosed limitation.</b> <see cref="ApproveAsync"/> calls
/// <see cref="IPhotoMasterPromotionGateway.PromoteAsync"/> (creating a real, independently
/// audited BIO-PHO-001 <c>PhotoMaster</c>) BEFORE committing this capture's own
/// <see cref="CaptureStatus.Promoted"/> transition. Holding <see cref="BoothCaptureLockRegistry"/>
/// across the whole method serializes every mutation for this person, so the two writes are
/// never actually raced against each other in this single-process host; there is still no
/// cross-aggregate distributed transaction, so a process crash in the narrow window between a
/// successful promotion and this capture's own commit is a disclosed, unresolved gap - the same
/// class of "this module cannot prove more without becoming a different system" boundary
/// ADR-0005 Decision 4.6 already names for a different feature.
/// </summary>
internal sealed class CaptureService(
    ICurrentTenantProvider tenantProvider,
    IBoothCaptureRepository repository,
    IPersonExistenceGateway personGateway,
    IPhotoMasterPromotionGateway promotionGateway,
    ICaptureAuditSink auditSink,
    IIdempotencyStore idempotencyStore,
    BoothCaptureLockRegistry lockRegistry,
    TimeSpan? idempotencyWaitTimeout = null,
    TimeSpan? idempotencyWaitPollInterval = null)
{
    private const string SubmitOperation = "submit_capture";

    // TD-PHO-002-IDEMPOTENCY-RACE's own remediation: how long a claim-loser (a concurrent caller
    // whose Idempotency-Key is already owned by someone else's in-flight attempt) waits for that
    // owner's result before giving up with a 503 (ADR-0004/BIO-ARC-004's documented "claim-loser
    // waits bounded then idempotency_wait_timeout" contract). Both are injectable (constructor
    // parameters, defaulting to production-sized values) so a test can prove the timeout path
    // deterministically and fast, without actually waiting multiple real seconds.
    private readonly TimeSpan _idempotencyWaitTimeout = idempotencyWaitTimeout ?? TimeSpan.FromSeconds(5);
    private readonly TimeSpan _idempotencyWaitPollInterval = idempotencyWaitPollInterval ?? TimeSpan.FromMilliseconds(20);

    public async Task<BoothCapture> SubmitAsync(
        string personId, string deviceId, byte[] content, bool faceHeightCompliant, bool backgroundCompliant,
        string idempotencyKey, AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();

        if (!CanonicalId.IsPersonId(personId) || !await personGateway.ExistsAsync(personId, cancellationToken))
        {
            throw PhotoBoothError.PersonNotFound(personId);
        }

        var inspected = CaptureRasterInspector.Inspect(content);
        if (inspected is null)
        {
            throw PhotoBoothError.UnsupportedFormat();
        }

        // TD-PHO-002-IDEMPOTENCY-RACE: the original code checked only `claimed.State ==
        // Completed` - a shared InFlight record from a CONCURRENT caller looked identical to a
        // claim this caller itself just won, so a second caller fell through and submitted a
        // second time regardless. `IdempotencyClaim.IsNewClaim` is the missing ownership signal:
        // only the true owner ever breaks out of this loop to proceed below. Every other caller
        // (someone else's claim is in flight) waits, bounded by `_idempotencyWaitTimeout`,
        // re-attempting its own `Claim` each iteration.
        var fingerprint = ComputeFingerprint(personId, deviceId, content, faceHeightCompliant, backgroundCompliant);
        var deadline = DateTime.UtcNow + _idempotencyWaitTimeout;
        while (true)
        {
            var claim = idempotencyStore.Claim(tenantId, SubmitOperation, idempotencyKey, fingerprint);
            if (claim.Record.RequestFingerprint != fingerprint)
            {
                throw PhotoBoothError.IdempotencyKeyReuse();
            }

            if (claim.Record.State == IdempotencyRecordState.Completed)
            {
                return (BoothCapture)claim.Record.Result!;
            }

            if (claim.IsNewClaim)
            {
                break;
            }

            if (DateTime.UtcNow >= deadline)
            {
                throw PhotoBoothError.IdempotencyWaitTimeout(idempotencyKey);
            }

            await Task.Delay(_idempotencyWaitPollInterval, cancellationToken);
        }

        try
        {
            using var _ = await lockRegistry.AcquireAsync(personId, cancellationToken);

            var existing = await repository.ListForPersonAsync(tenantId, personId, cancellationToken);
            var nextSequence = existing.Count == 0 ? 1 : existing.Max(c => c.SequenceNumber) + 1;

            var verdict = CaptureQualityGate.Evaluate(inspected, CaptureSpecification.Current, faceHeightCompliant, backgroundCompliant);
            var now = UtcInstant.Now();
            var status = verdict.Passed ? CaptureStatus.Pending : CaptureStatus.Rejected;

            var capture = new BoothCapture(
                personId, tenantId, deviceId, nextSequence, inspected.Format, inspected.WidthPx, inspected.HeightPx, inspected.NativeDpi,
                faceHeightCompliant, backgroundCompliant, content, status, verdict.Passed ? [] : verdict.FailureReasons,
                promotedMasterVersion: null, version: 1, submittedAt: now, decidedAt: verdict.Passed ? null : now);

            await AuditAsync(
                tenantId, actor, "photo_booth.capture.submit", personId,
                verdict.Passed ? "capture_submitted" : "capture_rejected_at_submission", correlationId, causationId: null,
                detail: new Dictionary<string, (object?, string)>
                {
                    ["device_id"] = (deviceId, "internal"),
                    ["sequence_number"] = (nextSequence, "internal"),
                    ["status"] = (status.ToString(), "internal"),
                    ["width_px"] = (inspected.WidthPx, "internal"),
                    ["height_px"] = (inspected.HeightPx, "internal"),
                },
                cancellationToken);

            var created = await repository.CreateAsync(capture, CancellationToken.None);
            idempotencyStore.Complete(tenantId, SubmitOperation, idempotencyKey, created);
            return created;
        }
        catch
        {
            idempotencyStore.Release(tenantId, SubmitOperation, idempotencyKey);
            throw;
        }
    }

    public async Task<BoothCapture> ApproveAsync(
        string personId, int sequenceNumber, int expectedVersion, AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        using var _ = await lockRegistry.AcquireAsync(personId, cancellationToken);

        var current = await repository.GetAsync(tenantId, personId, sequenceNumber, cancellationToken)
            ?? throw PhotoBoothError.CaptureNotFound(personId, sequenceNumber);

        if (current.Status != CaptureStatus.Pending)
        {
            throw PhotoBoothError.CaptureNotPending(personId, sequenceNumber, current.Status);
        }

        if (current.Version != expectedVersion)
        {
            throw PhotoBoothError.CaptureConcurrencyConflict(personId, sequenceNumber, expectedVersion, current.Version);
        }

        // BIO-PHO-002-AC04's actual promotion: the only call anywhere in this project to
        // IPhotoMasterPromotionGateway, reachable only from this exact, already-validated path.
        var masterVersion = await promotionGateway.PromoteAsync(personId, current.Content, actor, correlationId, cancellationToken);

        var now = UtcInstant.Now();
        var updated = new BoothCapture(
            personId, tenantId, current.DeviceId, sequenceNumber, current.Format, current.WidthPx, current.HeightPx, current.NativeDpi,
            current.FaceHeightCompliant, current.BackgroundCompliant, current.Content, CaptureStatus.Promoted,
            rejectionReasons: [], promotedMasterVersion: masterVersion, version: current.Version + 1, submittedAt: current.SubmittedAt, decidedAt: now);

        await AuditAsync(
            tenantId, actor, "photo_booth.capture.approve", personId, "capture_approved_and_promoted", correlationId, causationId: null,
            detail: new Dictionary<string, (object?, string)>
            {
                ["sequence_number"] = (sequenceNumber, "internal"),
                ["promoted_master_version"] = (masterVersion, "internal"),
            },
            cancellationToken);

        return await repository.ReplaceAsync(current, updated, CancellationToken.None);
    }

    public async Task<BoothCapture> RejectAsync(
        string personId, int sequenceNumber, string reason, int expectedVersion, AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw PhotoBoothError.Validation([new ValidationErrorItem("reason", "required", "reason is required to reject a capture")]);
        }

        using var _ = await lockRegistry.AcquireAsync(personId, cancellationToken);

        var current = await repository.GetAsync(tenantId, personId, sequenceNumber, cancellationToken)
            ?? throw PhotoBoothError.CaptureNotFound(personId, sequenceNumber);

        if (current.Status != CaptureStatus.Pending)
        {
            throw PhotoBoothError.CaptureNotPending(personId, sequenceNumber, current.Status);
        }

        if (current.Version != expectedVersion)
        {
            throw PhotoBoothError.CaptureConcurrencyConflict(personId, sequenceNumber, expectedVersion, current.Version);
        }

        var now = UtcInstant.Now();
        var updated = new BoothCapture(
            personId, tenantId, current.DeviceId, sequenceNumber, current.Format, current.WidthPx, current.HeightPx, current.NativeDpi,
            current.FaceHeightCompliant, current.BackgroundCompliant, current.Content, CaptureStatus.Rejected,
            rejectionReasons: [reason], promotedMasterVersion: null, version: current.Version + 1, submittedAt: current.SubmittedAt, decidedAt: now);

        await AuditAsync(
            tenantId, actor, "photo_booth.capture.reject", personId, "capture_rejected_by_reviewer", correlationId, causationId: null,
            detail: new Dictionary<string, (object?, string)> { ["sequence_number"] = (sequenceNumber, "internal") },
            cancellationToken);

        return await repository.ReplaceAsync(current, updated, CancellationToken.None);
    }

    public Task<BoothCapture?> GetAsync(string personId, int sequenceNumber, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        return repository.GetAsync(tenantId, personId, sequenceNumber, cancellationToken);
    }

    public Task<IReadOnlyList<BoothCapture>> ListForPersonAsync(string personId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        return repository.ListForPersonAsync(tenantId, personId, cancellationToken);
    }

    private static string ComputeFingerprint(string personId, string deviceId, byte[] content, bool faceHeightCompliant, bool backgroundCompliant)
    {
        var contentHash = Convert.ToHexStringLower(SHA256.HashData(content));
        return $"{personId}|{deviceId}|{contentHash}|{faceHeightCompliant}|{backgroundCompliant}";
    }

    private async Task AuditAsync(
        string tenantId, AuditActor actor, string action, string personId, string reasonCode, string correlationId, string? causationId,
        IReadOnlyDictionary<string, (object? Value, string Classification)> detail, CancellationToken cancellationToken)
    {
        var auditEvent = CaptureAuditEventBuilder.Build(
            tenantId, actor, action, new AuditTarget("photo_booth_capture", personId), AuditOutcome.Allowed, reasonCode, correlationId, causationId, detail);

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
            throw PhotoBoothError.AuditUnavailable(); // total outage: cannot even verify
        }

        if (!exists)
        {
            throw PhotoBoothError.AuditUnavailable(); // confirmed: genuinely never persisted
        }

        // else: confirmed durably present despite the throw on WriteAsync - proceed as a
        // success, so the caller's subsequent commit makes this event's claim true.
    }

    private string RequireTenant() => tenantProvider.TenantId ?? throw PhotoBoothError.TenantContextMissing();
}

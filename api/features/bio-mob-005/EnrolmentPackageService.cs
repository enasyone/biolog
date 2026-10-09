using System.Security.Cryptography;
using System.Text;
using Biolog.Features.OfflineEnrolment.Api;
using Biolog.Features.OfflineEnrolment.Audit;
using Biolog.Features.OfflineEnrolment.Domain;
using Biolog.Features.OfflineEnrolment.Identifiers;

namespace Biolog.Features.OfflineEnrolment;

/// <summary>
/// Application service for BIO-MOB-005's server side. AC01 (capture-policy resolve/admin-write),
/// AC02 is entirely the mobile client's own concern (the package travels encrypted at rest inside
/// the same SQLCipher-encrypted local database every other offline queue already uses - see
/// <c>mobile/lib/core/enrolment/</c> - there is nothing for this service to additionally encrypt),
/// AC03 (central duplicate scan, synchronous within package intake) and AC04 (the human
/// duplicate-resolution decision).
/// </summary>
public sealed class EnrolmentPackageService(
    IEnrolmentCapturePolicyRepository capturePolicyRepository,
    IEnrolmentPackageRepository packageRepository,
    IEnrolmentDuplicateCandidateRepository duplicateCandidateRepository,
    EnrolmentDuplicateReviewService duplicateReviewService,
    IIdempotencyStore idempotencyStore,
    IEnrolmentAuditSink auditSink,
    TimeSpan? idempotencyWaitTimeout = null, TimeSpan? idempotencyWaitPollInterval = null)
{
    private const string SubmitOperation = "enrolment_package_submit";

    // Idempotency-claim-race remediation: how long a claim-loser (a concurrent caller whose
    // Idempotency-Key is already owned by someone else's in-flight attempt) waits for that
    // owner's result before giving up with a 503. Both are injectable (constructor parameters,
    // defaulting to production-sized values) so a test can prove the timeout path
    // deterministically and fast, without actually waiting multiple real seconds.
    private readonly TimeSpan _idempotencyWaitTimeout = idempotencyWaitTimeout ?? TimeSpan.FromSeconds(5);
    private readonly TimeSpan _idempotencyWaitPollInterval = idempotencyWaitPollInterval ?? TimeSpan.FromMilliseconds(20);

    public async Task<CapturePolicyResponse> GetCapturePolicyAsync(ResolvedDevice device, CancellationToken cancellationToken)
    {
        var policy = await capturePolicyRepository.GetAsync(device.TenantId, cancellationToken);
        return CapturePolicyResponse.From(policy);
    }

    /// <summary>Ambient-tenant/actor/provenance/RBAC-gated admin write - see <c>Api/EnrolmentEndpoints.cs</c>. Rejects an unknown field/modality key outright rather than silently ignoring it (the same "fail loud on an unrecognised value" discipline this codebase applies to every closed-vocabulary field).</summary>
    public async Task<CapturePolicyResponse> SetCapturePolicyAsync(string tenantId, SetCapturePolicyRequest request, AuditActor actor, string correlationId, CancellationToken cancellationToken)
    {
        List<ValidationErrorItem>? errors = null;
        void Add(string field, string code, string message) => (errors ??= []).Add(new ValidationErrorItem(field, code, message));

        var allowedFields = new HashSet<string>(request.AllowedFields, StringComparer.Ordinal);
        foreach (var field in allowedFields)
        {
            if (!KnownFields.All.Contains(field))
            {
                Add("allowed_fields", "unknown_field", $"'{field}' is not a recognised enrolment field");
            }
        }

        var allowedModalities = new HashSet<string>(request.AllowedBiometricModalities, StringComparer.Ordinal);
        foreach (var modality in allowedModalities)
        {
            if (!KnownBiometricModalities.All.Contains(modality))
            {
                Add("allowed_biometric_modalities", "unknown_modality", $"'{modality}' is not a recognised biometric modality");
            }
        }

        if (errors is not null)
        {
            throw EnrolmentError.Validation(errors);
        }

        var policy = await capturePolicyRepository.SetAsync(tenantId, allowedFields, request.AllowPhotoCapture, allowedModalities, cancellationToken);

        await AuditAsync(tenantId, actor, "offline_enrolment.capture_policy.update", new AuditTarget("enrolment_capture_policy", tenantId), AuditOutcome.Allowed, "policy_updated", correlationId, cancellationToken);

        return CapturePolicyResponse.From(policy);
    }

    /// <summary>
    /// Audit-before-commit (ADR-0003 Decision 7), the same discipline
    /// <c>OfflineMutationIntakeService</c>/<c>AccessCacheService</c> each already establish: the
    /// idempotency claim happens first, then the audit write is confirmed durable BEFORE the
    /// package is persisted - the real point of no return. The AC03 duplicate scan runs
    /// synchronously, immediately after that commit, still inside this same request - "duplicate/
    /// conflict checks re-run centrally after sync" happens the moment the package reaches this
    /// central service, not on some later batch job.
    /// </summary>
    public async Task<(EnrolmentPackageAckResponse Result, bool Replayed)> SubmitPackageAsync(
        ResolvedDevice device, EnrolmentPackageSubmitRequest request, string idempotencyKey,
        AuditActor actor, string correlationId, CancellationToken cancellationToken)
    {
        Validate(request);

        if (request.TenantId != device.TenantId)
        {
            throw EnrolmentError.TenantMismatch(request.TenantId, device.TenantId);
        }

        if (request.DeviceId != device.DeviceId)
        {
            throw EnrolmentError.DeviceMismatch(request.DeviceId, device.DeviceId);
        }

        var policy = await capturePolicyRepository.GetAsync(device.TenantId, cancellationToken);
        EnforcePolicy(request, policy);

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
            claim = idempotencyStore.Claim(device.TenantId, SubmitOperation, idempotencyKey, fingerprint);
            if (claim.Record.RequestFingerprint != fingerprint)
            {
                throw EnrolmentError.IdempotencyKeyReuse();
            }

            if (claim.Record.State == IdempotencyRecordState.Completed)
            {
                return ((EnrolmentPackageAckResponse)claim.Record.Result!, Replayed: true);
            }

            if (claim.IsNewClaim)
            {
                break;
            }

            if (DateTime.UtcNow >= deadline)
            {
                throw EnrolmentError.IdempotencyWaitTimeout(idempotencyKey);
            }

            await Task.Delay(_idempotencyWaitPollInterval, cancellationToken);
        }

        var receivedAt = UtcInstant.Now();
        var package = new EnrolmentPackage
        {
            Id = request.PackageId,
            TenantId = device.TenantId,
            DeviceId = device.DeviceId,
            FieldValues = request.FieldValues,
            PhotoCaptured = request.PhotoCaptured,
            PhotoContentBase64 = request.PhotoContentBase64,
            BiometricModalitiesCaptured = request.BiometricModalitiesCaptured,
            BiometricPayloadsBase64 = request.BiometricPayloadsBase64,
            Status = EnrolmentPackageStatus.Submitted,
            SubmittedAt = request.SubmittedAt,
        };

        try
        {
            await AuditAsync(device.TenantId, actor, "offline_enrolment.package.intake", new AuditTarget("enrolment_package", package.Id), AuditOutcome.Allowed, "submitted", correlationId, cancellationToken);
        }
        catch
        {
            idempotencyStore.Release(device.TenantId, SubmitOperation, idempotencyKey);
            throw;
        }

        // Audit write confirmed durable - the real point of no return. CancellationToken.None:
        // a caller cancellation arriving after this point must not abort a create/scan whose
        // audit event already durably claims it happened.
        await packageRepository.CreateAsync(package, CancellationToken.None);

        package = await RunDuplicateScanAsync(package, correlationId, CancellationToken.None);

        var result = EnrolmentPackageAckResponse.From(package, receivedAt);
        idempotencyStore.Complete(device.TenantId, SubmitOperation, idempotencyKey, result);
        return (result, Replayed: false);
    }

    /// <summary>BIO-MOB-005-AC03: scans every already-<c>Accepted</c> package in this tenant for a signal match. A match holds the new package in <see cref="EnrolmentPackageStatus.PendingReview"/> and raises an <see cref="EnrolmentDuplicateCandidate"/> (AC04); no match accepts it outright.</summary>
    private async Task<EnrolmentPackage> RunDuplicateScanAsync(EnrolmentPackage package, string correlationId, CancellationToken cancellationToken)
    {
        var acceptedPackages = await packageRepository.ListAcceptedAsync(package.TenantId, cancellationToken);
        var signal = duplicateReviewService.FindMatch(package, acceptedPackages, out var matchedPackageId);

        if (signal is null)
        {
            var accepted = package with { Status = EnrolmentPackageStatus.Accepted, DecidedAt = UtcInstant.Now(), DecisionReasonCode = "no_duplicate_signal" };
            await packageRepository.ReplaceAsync(accepted, cancellationToken);
            return accepted;
        }

        var candidate = new EnrolmentDuplicateCandidate
        {
            Id = CanonicalId.NewDuplicateCandidateId(),
            TenantId = package.TenantId,
            PackageId = package.Id,
            MatchedPackageId = matchedPackageId,
            SignalSource = signal.Value,
            Status = DuplicateCandidateStatus.PendingReview,
            RaisedAt = UtcInstant.Now(),
        };
        await duplicateCandidateRepository.CreateAsync(candidate, cancellationToken);

        await AuditAsync(package.TenantId, new AuditActor(ActorType.System, "offline-enrolment-duplicate-scan"), "offline_enrolment.duplicate_candidate.raise", new AuditTarget("enrolment_duplicate_candidate", candidate.Id), AuditOutcome.Denied, "duplicate_signal_matched", correlationId, cancellationToken);

        var held = package with { Status = EnrolmentPackageStatus.PendingReview };
        await packageRepository.ReplaceAsync(held, cancellationToken);
        return held;
    }

    public async Task<EnrolmentPackageListResponse> ListPackagesByStatusAsync(string tenantId, EnrolmentPackageStatus status, CancellationToken cancellationToken)
    {
        var packages = await packageRepository.ListByStatusAsync(tenantId, status, cancellationToken);
        return new EnrolmentPackageListResponse { Items = [.. packages.Select(EnrolmentPackageResponse.From)] };
    }

    public async Task<DuplicateCandidateListResponse> ListPendingDuplicateCandidatesAsync(string tenantId, CancellationToken cancellationToken)
    {
        var candidates = await duplicateCandidateRepository.ListByStatusAsync(tenantId, DuplicateCandidateStatus.PendingReview, cancellationToken);
        return new DuplicateCandidateListResponse { Items = [.. candidates.Select(DuplicateCandidateResponse.From)] };
    }

    /// <summary>
    /// BIO-MOB-005-AC04: the only way a <see cref="EnrolmentPackageStatus.PendingReview"/> package
    /// leaves that state. <paramref name="request"/>.<c>ReasonCode</c> is mandatory - there is no
    /// decision path that skips recording why.
    /// </summary>
    public async Task<DuplicateCandidateResponse> DecideDuplicateCandidateAsync(string tenantId, string candidateId, DuplicateCandidateDecisionRequest request, AuditActor actor, string correlationId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ReasonCode))
        {
            throw EnrolmentError.DecisionReasonRequired();
        }

        var confirmDuplicate = request.Decision == "CONFIRMED_DUPLICATE";
        var confirmDistinct = request.Decision == "CONFIRMED_DISTINCT";
        if (!confirmDuplicate && !confirmDistinct)
        {
            throw EnrolmentError.Validation([new ValidationErrorItem("decision", "invalid_value", "must be one of CONFIRMED_DUPLICATE, CONFIRMED_DISTINCT")]);
        }

        var candidate = await duplicateCandidateRepository.GetAsync(tenantId, candidateId, cancellationToken)
            ?? throw EnrolmentError.DuplicateCandidateNotFound(candidateId);

        if (candidate.Status != DuplicateCandidateStatus.PendingReview)
        {
            throw EnrolmentError.DuplicateCandidateNotPending(candidateId);
        }

        var package = await packageRepository.GetAsync(tenantId, candidate.PackageId, cancellationToken)
            ?? throw EnrolmentError.PackageNotFound(candidate.PackageId);

        var decidedAt = UtcInstant.Now();
        var newCandidateStatus = confirmDuplicate ? DuplicateCandidateStatus.ConfirmedDuplicate : DuplicateCandidateStatus.ConfirmedDistinct;
        var newPackageStatus = confirmDuplicate ? EnrolmentPackageStatus.RejectedDuplicate : EnrolmentPackageStatus.Accepted;

        var decidedCandidate = candidate with { Status = newCandidateStatus, DecidedAt = decidedAt, DecidedByActorId = actor.Id, ReasonCode = request.ReasonCode };
        var decidedPackage = package with { Status = newPackageStatus, DecidedAt = decidedAt, DecisionReasonCode = request.ReasonCode };

        await AuditAsync(tenantId, actor, "offline_enrolment.duplicate_candidate.decide", new AuditTarget("enrolment_duplicate_candidate", candidateId), confirmDuplicate ? AuditOutcome.Denied : AuditOutcome.Allowed, request.ReasonCode, correlationId, cancellationToken);

        await duplicateCandidateRepository.ReplaceAsync(decidedCandidate, CancellationToken.None);
        await packageRepository.ReplaceAsync(decidedPackage, CancellationToken.None);

        return DuplicateCandidateResponse.From(decidedCandidate);
    }

    private static void Validate(EnrolmentPackageSubmitRequest request)
    {
        List<ValidationErrorItem>? errors = null;
        void Add(string field, string code, string message) => (errors ??= []).Add(new ValidationErrorItem(field, code, message));

        if (!CanonicalId.IsEnrolmentPackageId(request.PackageId)) Add("package_id", "invalid_format", "must be a canonical enp_<uuidv7> id");
        if (!CanonicalId.IsTenantId(request.TenantId)) Add("tenant_id", "invalid_format", "must be a canonical ten_<uuidv7> id");
        if (!CanonicalId.IsDeviceId(request.DeviceId)) Add("device_id", "invalid_format", "must be a canonical dev_<uuidv7> id");
        if (string.IsNullOrWhiteSpace(request.SubmittedAt)) Add("submitted_at", "required", "submitted_at is required");
        if (request.FieldValues.Count == 0 && !request.PhotoCaptured && request.BiometricModalitiesCaptured.Count == 0)
        {
            Add("field_values", "empty_package", "an enrolment package must capture at least one field, a photo, or a biometric");
        }
        if (request.PhotoCaptured && string.IsNullOrWhiteSpace(request.PhotoContentBase64))
        {
            Add("photo_content_base64", "required", "photo_content_base64 is required when photo_captured is true");
        }

        if (errors is not null)
        {
            throw EnrolmentError.Validation(errors);
        }
    }

    /// <summary>AC01 enforced server-side, defense-in-depth on top of the mobile UI's own policy-gated field visibility.</summary>
    private static void EnforcePolicy(EnrolmentPackageSubmitRequest request, EnrolmentCapturePolicy policy)
    {
        foreach (var field in request.FieldValues.Keys)
        {
            if (!policy.AllowedFields.Contains(field))
            {
                throw EnrolmentError.FieldNotPermitted(field);
            }
        }

        if (request.PhotoCaptured && !policy.AllowPhotoCapture)
        {
            throw EnrolmentError.PhotoCaptureNotPermitted();
        }

        foreach (var modality in request.BiometricModalitiesCaptured)
        {
            if (!policy.AllowedBiometricModalities.Contains(modality))
            {
                throw EnrolmentError.BiometricModalityNotPermitted(modality);
            }
        }
    }

    /// <summary>Every identity-bearing claim is bound into the fingerprint, not only the package id - mirrors BIO-MOB-003-QA-02's/BIO-ACC-003's defense-in-depth reasoning: a replayed idempotency key with different content is a fingerprint mismatch (409), never silently treated as an identical replay.</summary>
    internal static string ComputeFingerprint(EnrolmentPackageSubmitRequest request)
    {
        var orderedFields = string.Join(",", request.FieldValues.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => $"{kv.Key}={kv.Value}"));
        var orderedModalities = string.Join(",", request.BiometricModalitiesCaptured.OrderBy(m => m, StringComparer.Ordinal));
        var canonical = $"{request.PackageId}|{request.TenantId}|{request.DeviceId}|{orderedFields}|{request.PhotoCaptured}|{request.PhotoContentBase64}|{orderedModalities}|{request.SubmittedAt}";
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private async Task AuditAsync(string tenantId, AuditActor actor, string action, AuditTarget target, AuditOutcome outcome, string reasonCode, string correlationId, CancellationToken cancellationToken)
    {
        var auditEvent = new EnrolmentAuditEvent(CanonicalId.NewEventId(), tenantId, actor, action, target, outcome, reasonCode, correlationId, DateTimeOffset.UtcNow);

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
            throw EnrolmentError.AuditUnavailable();
        }

        if (!exists)
        {
            throw EnrolmentError.AuditUnavailable();
        }
    }
}

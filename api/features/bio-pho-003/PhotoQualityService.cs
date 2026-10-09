using Biolog.Features.PhotoQuality.Api;
using Biolog.Features.PhotoQuality.Audit;
using Biolog.Features.PhotoQuality.Domain;
using Biolog.Features.PhotoQuality.Identifiers;
using Biolog.Features.PhotoQuality.Tenancy;

namespace Biolog.Features.PhotoQuality;

/// <summary>
/// Application service for BIO-PHO-003. Implements ADR-0003 Decision 7's audit-atomicity
/// pattern - established across every prior story in this repository - applied fresh here:
/// every mutating method holds an in-process, per-person-id lock
/// (<see cref="PhotoQualityCaptureLockRegistry"/>) across its whole read-validate-audit-commit
/// sequence, audits BEFORE committing, resolves an ambiguous audit-write throw via
/// <see cref="IPhotoQualityAuditSink.ExistsAsync"/> rather than assuming failure, and passes
/// <see cref="CancellationToken.None"/> to the repository commit once the audit write is
/// confirmed durable.
///
/// <b>AC01: a capture always starts PENDING, regardless of either quality verdict.</b> Unlike
/// BIO-PHO-002's own <c>CaptureService.SubmitAsync</c> (whose failing <c>CaptureQualityGate</c>
/// auto-rejects a booth submission before a record even exists - a fully automated device
/// pipeline with no human reviewer at submission time), this story's own user story is
/// explicitly "As enrolment staff, I need separate print-quality and biometric-suitability
/// assessments" - the staff member IS the reviewer, and both assessments are computed purely to
/// INFORM their decision (AC02), never to make it for them. <see cref="SubmitAsync"/> therefore
/// never rejects on its own; <see cref="ApproveAsync"/>/<see cref="RejectAsync"/> are the only
/// two places a capture ever leaves <c>Pending</c>, exactly mirroring BIO-PHO-002's own
/// approve/reject CAS shape for the DECISION half of that story.
///
/// <b>ZB-PHO003-02/03/04 and SY-PHO003-07 (security review, five rounds, resolved by Kemi's
/// architectural ruling): approval is resumable across every crash window by REMEMBERING this
/// feature's own attempt locally, never by RECONSTRUCTING it from BIO-PHO-001's own post-hoc
/// state.</b> Round 1's marker was written only AFTER <see cref="IPhotoMasterPromotionGateway.PromoteAsync"/>
/// succeeded, leaving the window between a successful promotion and that write unprotected.
/// Round 2 moved the marker BEFORE the call but identified "was this my promotion" by "latest
/// version beyond baseline, with matching content bytes" - round 3 found that provably wrong
/// (an unrelated append sharing identical content could be falsely claimed; a LATER unrelated
/// append could permanently bury this attempt's own earlier promotion). Round 3's fix - an exact
/// attempt-id lookup at BIO-PHO-001's own promotion-boundary audit event - hit round 4 (that
/// audit event is written BEFORE the repository commit it describes, so its existence alone
/// isn't proof the commit succeeded); round 4's fix (confirm the claimed version against
/// BIO-PHO-001's own repository) hit round 5 (confirming a version EXISTS is not confirming THIS
/// attempt produced it - an unrelated attempt could legitimately commit the identical version
/// number under the exact same "failed-then-retried" scenario the fix itself relies on).
///
/// **Kemi's ruling**: every one of those rounds was narrowing the SAME underlying mistake -
/// inferring ownership of an external side effect from the external system's own observable
/// state, which can never be fully disambiguated from outside it, since <c>PhotoMaster</c>
/// genuinely has no caller-supplied identity field and adding one is outside this story's own
/// authority. The real fix is for THIS feature to remember its own attempt locally instead -
/// exactly the same <see cref="Domain.IIdempotencyStore"/>/<c>IsNewClaim</c> shape BIO-PHO-002's
/// own TD-PHO-002-IDEMPOTENCY-RACE remediation already uses for an unrelated operation.
/// <see cref="ApproveAsync"/> records a durable INTENT marker
/// (<see cref="PhotoQualityCapture.PendingApprovalAttemptId"/>/
/// <see cref="PhotoQualityCapture.PendingApprovalActorId"/>/
/// <see cref="PhotoQualityCapture.PendingApprovalActorType"/>) BEFORE ever calling
/// <see cref="IPhotoMasterPromotionGateway.PromoteAsync"/> - a stable attempt id and the
/// IMMUTABLE initiating actor - then <c>Claim</c>s that SAME attempt id as an idempotency key
/// BEFORE calling <c>PromoteAsync</c>: a genuine first attempt executes once and <c>Complete</c>s
/// the claim with the resulting master version; a resumed retry (same OR a different authorized
/// actor) finds the claim already <c>Completed</c> and returns ITS stored result directly -
/// zero further calls to BIO-PHO-001 at all, no reconstruction needed. <see cref="IPhotoMasterPromotionGateway"/>
/// shrinks back to just <c>PromoteAsync</c> - no change to <c>PhotoMaster</c>, its repository, or
/// any shared contract. The audit record always credits the ORIGINAL initiating actor, never
/// whichever operator happens to complete a retry. <see cref="RejectAsync"/> refuses outright
/// once this marker exists - see its own body.
///
/// <b>Named residual (Kemi's own ruling, not a new gap, disclosed not chased further):</b> a
/// crash strictly between <c>PromoteAsync</c> succeeding and this method's own <c>Complete</c>
/// call leaves that claim stuck <c>InFlight</c> forever in the volatile, process-lifetime
/// idempotency store - a retry's own bounded poll-wait then times out and throws
/// (<see cref="PhotoQualityError.IdempotencyWaitTimeout"/>) rather than silently reusing wrong
/// data. The same "crash residual inert with volatile singletons" boundary every in-memory
/// placeholder in this codebase carries pending ADR-0005's real persistence - fails LOUD, not
/// silently wrong.
///
/// <b>Deliberately does not create a BIO-BIO-003 FaceBiometricReference.</b> Approving a photo
/// here only promotes a new <c>PhotoMaster</c> - it never calls BIO-BIO-003's own
/// <c>FaceEnrolmentService.CaptureAsync</c>. None of this story's five ACs ask for a biometric
/// reference to be created; AC01/AC02 only ask that a biometric SUITABILITY signal exists and
/// is visible. A later story that wants "approving a photo here also enrols the biometric
/// reference" is a new, disclosed decision for whoever owns that workflow, not a silent
/// side-effect this service adds for itself.
///
/// <b>Registration lifetime and locking.</b> Registered Scoped (reads a per-request
/// <see cref="ICurrentTenantProvider"/>) - see <see cref="PhotoQualityCaptureLockRegistry"/>'s
/// header.
/// </summary>
internal sealed class PhotoQualityService(
    ICurrentTenantProvider tenantProvider,
    IPhotoQualityCaptureRepository repository,
    IPersonExistenceGateway personGateway,
    IBiometricSuitabilityGateway biometricGateway,
    IPhotoMasterPromotionGateway promotionGateway,
    IPhotoQualityAuditSink auditSink,
    PhotoQualityCaptureLockRegistry lockRegistry,
    IIdempotencyStore idempotencyStore,
    TimeSpan? idempotencyWaitTimeout = null,
    TimeSpan? idempotencyWaitPollInterval = null)
{
    private const string ApproveOperation = "approve_photo_quality_capture";

    // Kemi's ruling (ZB-PHO003-04): how long a claim-loser (a concurrent retry whose attempt id
    // is already owned by an in-flight attempt) waits for that owner's result before giving up
    // with idempotency_wait_timeout (503) - the same bounded-wait contract BIO-PHO-002's own
    // TD-PHO-002-IDEMPOTENCY-RACE remediation already established. Both are injectable (trailing
    // optional constructor parameters, defaulting to production-sized values) so a test can
    // prove the timeout path deterministically and fast.
    private readonly TimeSpan _idempotencyWaitTimeout = idempotencyWaitTimeout ?? TimeSpan.FromSeconds(5);
    private readonly TimeSpan _idempotencyWaitPollInterval = idempotencyWaitPollInterval ?? TimeSpan.FromMilliseconds(20);


    public async Task<PhotoQualityCapture> SubmitAsync(
        string personId, byte[] content, AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();

        if (!CanonicalId.IsPersonId(personId) || !await personGateway.ExistsAsync(personId, cancellationToken))
        {
            throw PhotoQualityError.PersonNotFound(personId);
        }

        var inspected = PhotoQualityRasterInspector.Inspect(content);
        if (inspected is null)
        {
            throw PhotoQualityError.UnsupportedFormat();
        }

        var printVerdict = PrintQualityGate.Evaluate(inspected, PrintQualitySpecification.Current);
        var biometricResult = await biometricGateway.AssessAsync(content, cancellationToken);

        using var _ = await lockRegistry.AcquireAsync(personId, cancellationToken);

        var existing = await repository.ListForPersonAsync(tenantId, personId, cancellationToken);
        var nextSequence = existing.Count == 0 ? 1 : existing.Max(c => c.SequenceNumber) + 1;
        var now = UtcInstant.Now();

        var capture = new PhotoQualityCapture(
            personId, tenantId, nextSequence, inspected.Format, inspected.WidthPx, inspected.HeightPx, inspected.NativeDpi, content,
            printVerdict.Passed, printVerdict.Reasons, biometricResult.Passed, biometricResult.Reasons,
            status: PhotoQualityCaptureStatus.Pending, decisionReason: null, promotedMasterVersion: null, version: 1,
            submittedAt: now, decidedAt: null);

        await AuditOnceAsync(
            CanonicalId.NewEventId(), tenantId, actor, "photo_quality.capture.submit", personId, "capture_submitted_for_review", correlationId,
            detail: new Dictionary<string, (object?, string)>
            {
                ["sequence_number"] = (nextSequence, "internal"),
                ["print_quality_passed"] = (printVerdict.Passed, "internal"),
                ["biometric_suitability_passed"] = (biometricResult.Passed, "internal"),
                ["width_px"] = (inspected.WidthPx, "internal"),
                ["height_px"] = (inspected.HeightPx, "internal"),
            },
            cancellationToken);

        return await repository.CreateAsync(capture, CancellationToken.None);
    }

    public async Task<PhotoQualityCapture> ApproveAsync(
        string personId, int sequenceNumber, int expectedVersion, AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        using var _ = await lockRegistry.AcquireAsync(personId, cancellationToken);

        var current = await repository.GetAsync(tenantId, personId, sequenceNumber, cancellationToken)
            ?? throw PhotoQualityError.CaptureNotFound(personId, sequenceNumber);

        if (current.Status != PhotoQualityCaptureStatus.Pending)
        {
            throw PhotoQualityError.CaptureNotPending(personId, sequenceNumber, current.Status);
        }

        if (current.Version != expectedVersion)
        {
            throw PhotoQualityError.CaptureConcurrencyConflict(personId, sequenceNumber, expectedVersion, current.Version);
        }

        string attemptId;
        AuditActor initiatingActor;

        if (current.PendingApprovalAttemptId is { } existingAttemptId)
        {
            // ZB-PHO003-02 resumption: reuse the earlier attempt's own id and - critically - its
            // ORIGINAL initiating actor. A different operator retrying must never be attributed
            // the first operator's own decision.
            attemptId = existingAttemptId;
            initiatingActor = new AuditActor(current.PendingApprovalActorType!.Value, current.PendingApprovalActorId!);
        }
        else
        {
            // First attempt: durably record intent - actor and attempt id - BEFORE ever calling
            // PromoteAsync. A crash anywhere from this point onward (during the external call,
            // between it returning and anything else running, or later) is survivable:
            // resumption below resolves the true outcome by looking up THIS EXACT attempt id at
            // BIO-PHO-001's own promotion boundary (ZB-PHO003-03), never from anything that may
            // or may not have run after the original PromoteAsync call. Deliberately keeps the
            // SAME version number (not current.Version + 1): the per-person lock held for this
            // method's whole duration already makes this write race-free against any other
            // in-process mutation, and not bumping the version means a client retrying with its
            // original expected_version still matches on a resumed attempt below.
            attemptId = CanonicalId.NewEventId();
            initiatingActor = actor;

            var marked = new PhotoQualityCapture(
                personId, tenantId, sequenceNumber, current.Format, current.WidthPx, current.HeightPx, current.NativeDpi, current.Content,
                current.PrintQualityPassed, current.PrintQualityReasons, current.BiometricSuitabilityPassed, current.BiometricSuitabilityReasons,
                status: PhotoQualityCaptureStatus.Pending, decisionReason: null, promotedMasterVersion: null,
                version: current.Version, submittedAt: current.SubmittedAt, decidedAt: null,
                pendingApprovalAttemptId: attemptId, pendingApprovalActorId: initiatingActor.Id, pendingApprovalActorType: initiatingActor.Type);
            current = await repository.ReplaceAsync(current, marked, CancellationToken.None);
        }

        // Kemi's ruling (ZB-PHO003-04): REMEMBER whether this exact attempt already promoted,
        // locally, rather than reconstructing it from BIO-PHO-001's own post-hoc state. The
        // attempt id IS the idempotency key - stable across every resumption, never reused
        // across two different decisions. IsNewClaim is the ownership signal (not merely
        // State == Completed): only the true owner of a fresh claim ever proceeds to actually
        // call PromoteAsync; every other caller for the SAME in-flight attempt waits, bounded,
        // re-attempting its own Claim each iteration - the identical shape BIO-PHO-002's own
        // TD-PHO-002-IDEMPOTENCY-RACE remediation already established.
        var fingerprint = $"{personId}\u0000{sequenceNumber}";
        var deadline = DateTime.UtcNow + _idempotencyWaitTimeout;
        int masterVersion;
        while (true)
        {
            var claim = idempotencyStore.Claim(tenantId, ApproveOperation, attemptId, fingerprint);
            if (claim.Record.RequestFingerprint != fingerprint)
            {
                // Unreachable under correct code: attemptId is server-generated fresh per
                // decision, never caller-supplied, so it can never legitimately collide with a
                // different fingerprint the way a caller-supplied Idempotency-Key could.
                throw new InvalidOperationException($"photo_quality.approve idempotency key collision for attempt id '{attemptId}' - this should be structurally impossible");
            }

            if (claim.Record.State == IdempotencyRecordState.Completed)
            {
                masterVersion = (int)claim.Record.Result!;
                break;
            }

            if (claim.IsNewClaim)
            {
                try
                {
                    // BIO-PHO-003-AC03's actual promotion: the only call anywhere in this
                    // project to IPhotoMasterPromotionGateway.PromoteAsync, reachable only from
                    // this exact, already-validated, claim-owning path.
                    masterVersion = await promotionGateway.PromoteAsync(personId, current.Content, initiatingActor, attemptId, cancellationToken);
                    idempotencyStore.Complete(tenantId, ApproveOperation, attemptId, masterVersion);
                }
                catch
                {
                    idempotencyStore.Release(tenantId, ApproveOperation, attemptId);
                    throw;
                }

                break;
            }

            if (DateTime.UtcNow >= deadline)
            {
                throw PhotoQualityError.IdempotencyWaitTimeout(attemptId);
            }

            await Task.Delay(_idempotencyWaitPollInterval, cancellationToken);
        }

        await AuditOnceAsync(
            attemptId, tenantId, initiatingActor, "photo_quality.capture.approve", personId, "capture_approved_by_operator", correlationId,
            detail: new Dictionary<string, (object?, string)>
            {
                ["sequence_number"] = (sequenceNumber, "internal"),
                ["promoted_master_version"] = (masterVersion, "internal"),
                ["print_quality_passed"] = (current.PrintQualityPassed, "internal"),
                ["biometric_suitability_passed"] = (current.BiometricSuitabilityPassed, "internal"),
            },
            cancellationToken);

        var now = UtcInstant.Now();
        var updated = new PhotoQualityCapture(
            personId, tenantId, sequenceNumber, current.Format, current.WidthPx, current.HeightPx, current.NativeDpi, current.Content,
            current.PrintQualityPassed, current.PrintQualityReasons, current.BiometricSuitabilityPassed, current.BiometricSuitabilityReasons,
            status: PhotoQualityCaptureStatus.Approved, decisionReason: null, promotedMasterVersion: masterVersion,
            version: current.Version + 1, submittedAt: current.SubmittedAt, decidedAt: now);

        return await repository.ReplaceAsync(current, updated, CancellationToken.None);
    }

    public async Task<PhotoQualityCapture> RejectAsync(
        string personId, int sequenceNumber, string reason, int expectedVersion, AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw PhotoQualityError.Validation([new ValidationErrorItem("reason", "required", "reason is required to reject a capture")]);
        }

        using var _ = await lockRegistry.AcquireAsync(personId, cancellationToken);

        var current = await repository.GetAsync(tenantId, personId, sequenceNumber, cancellationToken)
            ?? throw PhotoQualityError.CaptureNotFound(personId, sequenceNumber);

        if (current.Status != PhotoQualityCaptureStatus.Pending)
        {
            throw PhotoQualityError.CaptureNotPending(personId, sequenceNumber, current.Status);
        }

        if (current.Version != expectedVersion)
        {
            throw PhotoQualityError.CaptureConcurrencyConflict(personId, sequenceNumber, expectedVersion, current.Version);
        }

        // ZB-PHO003-02: an approval attempt is already in progress for this capture (its
        // durable intent marker is set) - a prior, interrupted Approve may already have
        // promoted a real BIO-PHO-001 PhotoMaster, or may be about to resolve one on its own
        // next retry. Rejecting now risks orphaning that promotion with no capture ever
        // reaching Approved. The only safe path forward is to retry Approve, which resumes and
        // finalizes using the recorded marker.
        if (current.PendingApprovalAttemptId is not null)
        {
            throw PhotoQualityError.ApprovalAlreadyInProgress(personId, sequenceNumber);
        }

        var now = UtcInstant.Now();
        var updated = new PhotoQualityCapture(
            personId, tenantId, sequenceNumber, current.Format, current.WidthPx, current.HeightPx, current.NativeDpi, current.Content,
            current.PrintQualityPassed, current.PrintQualityReasons, current.BiometricSuitabilityPassed, current.BiometricSuitabilityReasons,
            status: PhotoQualityCaptureStatus.Rejected, decisionReason: reason, promotedMasterVersion: null,
            version: current.Version + 1, submittedAt: current.SubmittedAt, decidedAt: now);

        await AuditOnceAsync(
            CanonicalId.NewEventId(), tenantId, actor, "photo_quality.capture.reject", personId, "capture_rejected_by_operator", correlationId,
            detail: new Dictionary<string, (object?, string)> { ["sequence_number"] = (sequenceNumber, "internal") },
            cancellationToken);

        return await repository.ReplaceAsync(current, updated, CancellationToken.None);
    }

    public Task<PhotoQualityCapture?> GetAsync(string personId, int sequenceNumber, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        return repository.GetAsync(tenantId, personId, sequenceNumber, cancellationToken);
    }

    public Task<IReadOnlyList<PhotoQualityCapture>> ListForPersonAsync(string personId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        return repository.ListForPersonAsync(tenantId, personId, cancellationToken);
    }

    /// <summary>
    /// ZB-PHO003-02: writes an audit event under an explicit, caller-supplied <paramref name="eventId"/>
    /// rather than always minting a fresh one - and checks <see cref="IPhotoQualityAuditSink.ExistsAsync"/>
    /// FIRST, before ever attempting a write. On a resumed approval retry, the caller passes the
    /// SAME <paramref name="eventId"/> recorded by the earlier attempt
    /// (<see cref="PhotoQualityCapture.PendingApprovalAttemptId"/>); if that attempt's own
    /// write already durably succeeded, this proactive check finds it and returns without writing
    /// a second record - "one truthful terminal decision audit" holds even under a retry, not
    /// just the original ambiguous-failure verification below (which still covers THIS call's own
    /// write genuinely throwing).
    /// </summary>
    private async Task AuditOnceAsync(
        string eventId, string tenantId, AuditActor actor, string action, string personId, string reasonCode, string correlationId,
        IReadOnlyDictionary<string, (object? Value, string Classification)> detail, CancellationToken cancellationToken)
    {
        if (await auditSink.ExistsAsync(eventId, cancellationToken))
        {
            return; // already durably recorded by an earlier attempt for this exact event id
        }

        var auditEvent = PhotoQualityAuditEventBuilder.Build(
            tenantId, actor, action, new AuditTarget("photo_quality_capture", personId), AuditOutcome.Allowed, reasonCode, correlationId,
            causationId: null, detail, eventId);

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
            throw PhotoQualityError.AuditUnavailable(); // total outage: cannot even verify
        }

        if (!exists)
        {
            throw PhotoQualityError.AuditUnavailable(); // confirmed: genuinely never persisted
        }

        // else: confirmed durably present despite the throw on WriteAsync - proceed as a
        // success, so the caller's subsequent commit makes this event's claim true.
    }

    private string RequireTenant() => tenantProvider.TenantId ?? throw PhotoQualityError.TenantContextMissing();
}

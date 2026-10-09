// Developer tests for BIO-PHO-003. SYNTHETIC fixtures only - no real photo or biometric data.
using Biolog.Features.PhotoQuality.Audit;
using Biolog.Features.PhotoQuality.Domain;
using Biolog.Features.PhotoQuality.Identifiers;
using Xunit;

namespace Biolog.Features.PhotoQuality.Tests;

public sealed class PhotoQualityServiceTests
{
    private const string TenantId = "ten_018f2e5a-1234-7abc-89ab-0123456789ab";
    private const string PersonId = "per_018f2e5a-9c3d-7a41-9b2e-4f7c8d1a6e5b";
    private static readonly AuditActor StaffActor = new(ActorType.User, "staff_1");

    private static (
        PhotoQualityService Service, IPhotoQualityCaptureRepository Repository, InMemoryPhotoQualityAuditSink AuditSink,
        FakePhotoMasterPromotionGateway Promotion, FakeBiometricSuitabilityGateway Biometric)
        MakeService(string? tenantId = TenantId)
    {
        var repository = new InMemoryPhotoQualityCaptureRepository();
        var auditSink = new InMemoryPhotoQualityAuditSink();
        var promotion = new FakePhotoMasterPromotionGateway();
        var biometric = new FakeBiometricSuitabilityGateway();
        var service = new PhotoQualityService(
            new FakeCurrentTenantProvider(tenantId), repository, new FakePersonExistenceGateway(), biometric, promotion, auditSink,
            new PhotoQualityCaptureLockRegistry(), new InMemoryIdempotencyStore());
        return (service, repository, auditSink, promotion, biometric);
    }

    private static byte[] CompliantPrintContent() =>
        SyntheticImages.Png(PrintQualitySpecification.Current.FrameWidthPx, PrintQualitySpecification.Current.FrameHeightPx, PrintQualitySpecification.Current.MinimumDpi);

    private const string CorrelationId = "evt_018f2e5a-aaaa-7bbb-8ccc-000000000001";

    [Fact]
    public async Task AC01_a_photo_that_passes_print_but_fails_biometric_quality_is_recorded_pending_with_both_verdicts_distinct()
    {
        var (service, _, _, _, biometric) = MakeService();
        biometric.NextResult = new BiometricSuitabilityResult(false, ["no_face_detected: position your face in the frame and recapture."]);

        var capture = await service.SubmitAsync(PersonId, CompliantPrintContent(), StaffActor, CorrelationId, TestContext.Current.CancellationToken);

        Assert.Equal(PhotoQualityCaptureStatus.Pending, capture.Status);
        Assert.True(capture.PrintQualityPassed);
        Assert.False(capture.BiometricSuitabilityPassed);
        Assert.NotEmpty(capture.BiometricSuitabilityReasons);
        Assert.Empty(capture.PrintQualityReasons);
    }

    [Fact]
    public async Task AC01_a_photo_that_fails_print_but_passes_biometric_quality_is_recorded_pending_with_both_verdicts_distinct()
    {
        var (service, _, _, _, biometric) = MakeService();
        biometric.NextResult = new BiometricSuitabilityResult(true, []);
        var tooSmall = SyntheticImages.Png(10, 10);

        var capture = await service.SubmitAsync(PersonId, tooSmall, StaffActor, CorrelationId, TestContext.Current.CancellationToken);

        Assert.Equal(PhotoQualityCaptureStatus.Pending, capture.Status);
        Assert.False(capture.PrintQualityPassed);
        Assert.NotEmpty(capture.PrintQualityReasons);
        Assert.True(capture.BiometricSuitabilityPassed);
        Assert.Empty(capture.BiometricSuitabilityReasons);
    }

    [Fact]
    public async Task AC01_a_photo_that_fails_both_is_still_recorded_pending_never_auto_rejected()
    {
        var (service, _, _, _, biometric) = MakeService();
        biometric.NextResult = new BiometricSuitabilityResult(false, ["multiple_faces_detected (2): ensure only one person is in frame and recapture."]);
        var tooSmall = SyntheticImages.Png(10, 10);

        var capture = await service.SubmitAsync(PersonId, tooSmall, StaffActor, CorrelationId, TestContext.Current.CancellationToken);

        // BIO-PHO-003's own user story is enrolment-staff-driven review, not an automated booth
        // gate (BIO-PHO-002's own CaptureQualityGate) - a failing verdict on EITHER or BOTH
        // sides never auto-rejects; only an explicit operator decision does.
        Assert.Equal(PhotoQualityCaptureStatus.Pending, capture.Status);
        Assert.Null(capture.DecisionReason);
        Assert.Null(capture.PromotedMasterVersion);
    }

    [Fact]
    public async Task AC01_a_photo_that_passes_both_is_also_merely_pending_not_auto_approved()
    {
        var (service, _, _, promotion, biometric) = MakeService();
        biometric.NextResult = new BiometricSuitabilityResult(true, []);

        var capture = await service.SubmitAsync(PersonId, CompliantPrintContent(), StaffActor, CorrelationId, TestContext.Current.CancellationToken);

        Assert.Equal(PhotoQualityCaptureStatus.Pending, capture.Status);
        Assert.Empty(promotion.Promotions); // no promotion happens until an explicit ApproveAsync
    }

    [Fact]
    public async Task AC01_the_biometric_gateway_is_genuinely_consulted_on_every_submission()
    {
        var (service, _, _, _, biometric) = MakeService();

        await service.SubmitAsync(PersonId, CompliantPrintContent(), StaffActor, CorrelationId, TestContext.Current.CancellationToken);

        Assert.Equal(1, biometric.CallCount);
    }

    [Fact]
    public async Task AC02_reasons_are_visible_for_every_failed_check_on_either_side()
    {
        var (service, _, _, _, biometric) = MakeService();
        biometric.NextResult = new BiometricSuitabilityResult(false, ["lighting_check_failed: recapture under even, diffuse lighting."]);
        var wrongAspect = SyntheticImages.Png(2000, 500, PrintQualitySpecification.Current.MinimumDpi);

        var capture = await service.SubmitAsync(PersonId, wrongAspect, StaffActor, CorrelationId, TestContext.Current.CancellationToken);

        Assert.Contains(capture.PrintQualityReasons, r => r.Contains("aspect ratio"));
        Assert.Contains(capture.BiometricSuitabilityReasons, r => r.Contains("lighting_check_failed"));
    }

    [Fact]
    public async Task AC03_approving_a_pending_capture_promotes_a_real_photo_master_and_is_audited()
    {
        var (service, _, auditSink, promotion, _) = MakeService();
        var submitted = await service.SubmitAsync(PersonId, CompliantPrintContent(), StaffActor, CorrelationId, TestContext.Current.CancellationToken);

        var approved = await service.ApproveAsync(PersonId, submitted.SequenceNumber, submitted.Version, StaffActor, CorrelationId, TestContext.Current.CancellationToken);

        Assert.Equal(PhotoQualityCaptureStatus.Approved, approved.Status);
        Assert.Equal(1, approved.PromotedMasterVersion);
        Assert.Single(promotion.Promotions);
        Assert.Contains(auditSink.Events, e => e.Action == "photo_quality.capture.approve");
    }

    [Fact]
    public async Task AC03_rejecting_a_pending_capture_requires_a_reason_and_is_audited_without_promoting_anything()
    {
        var (service, _, auditSink, promotion, _) = MakeService();
        var submitted = await service.SubmitAsync(PersonId, CompliantPrintContent(), StaffActor, CorrelationId, TestContext.Current.CancellationToken);

        var rejected = await service.RejectAsync(
            PersonId, submitted.SequenceNumber, "operator judged the background unacceptable despite passing checks", submitted.Version,
            StaffActor, CorrelationId, TestContext.Current.CancellationToken);

        Assert.Equal(PhotoQualityCaptureStatus.Rejected, rejected.Status);
        Assert.Equal("operator judged the background unacceptable despite passing checks", rejected.DecisionReason);
        Assert.Null(rejected.PromotedMasterVersion);
        Assert.Empty(promotion.Promotions);
        Assert.Contains(auditSink.Events, e => e.Action == "photo_quality.capture.reject");
    }

    [Fact]
    public async Task AC03_an_operator_may_approve_despite_a_failing_verdict_discretion_is_the_point()
    {
        var (service, _, _, promotion, biometric) = MakeService();
        biometric.NextResult = new BiometricSuitabilityResult(false, ["occlusion_check_failed: remove anything covering the face and recapture."]);
        var submitted = await service.SubmitAsync(PersonId, CompliantPrintContent(), StaffActor, CorrelationId, TestContext.Current.CancellationToken);

        var approved = await service.ApproveAsync(PersonId, submitted.SequenceNumber, submitted.Version, StaffActor, CorrelationId, TestContext.Current.CancellationToken);

        Assert.Equal(PhotoQualityCaptureStatus.Approved, approved.Status);
        Assert.False(approved.BiometricSuitabilityPassed); // the retained signal still shows the failure even though it was overridden
        Assert.Single(promotion.Promotions);
    }

    [Fact]
    public async Task AC03_rejecting_without_a_reason_is_refused()
    {
        var (service, _, _, _, _) = MakeService();
        var submitted = await service.SubmitAsync(PersonId, CompliantPrintContent(), StaffActor, CorrelationId, TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<PhotoQualityError>(() =>
            service.RejectAsync(PersonId, submitted.SequenceNumber, "   ", submitted.Version, StaffActor, CorrelationId, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AC03_a_capture_already_decided_cannot_be_decided_again()
    {
        var (service, _, _, _, _) = MakeService();
        var submitted = await service.SubmitAsync(PersonId, CompliantPrintContent(), StaffActor, CorrelationId, TestContext.Current.CancellationToken);
        var approved = await service.ApproveAsync(PersonId, submitted.SequenceNumber, submitted.Version, StaffActor, CorrelationId, TestContext.Current.CancellationToken);

        var error = await Assert.ThrowsAsync<PhotoQualityError>(() =>
            service.RejectAsync(PersonId, submitted.SequenceNumber, "too late", approved.Version, StaffActor, CorrelationId, TestContext.Current.CancellationToken));

        Assert.Equal("capture_not_pending", error.Code);
    }

    [Fact]
    public async Task AC03_a_stale_expected_version_is_refused_as_a_concurrency_conflict()
    {
        var (service, _, _, _, _) = MakeService();
        var submitted = await service.SubmitAsync(PersonId, CompliantPrintContent(), StaffActor, CorrelationId, TestContext.Current.CancellationToken);

        var error = await Assert.ThrowsAsync<PhotoQualityError>(() =>
            service.ApproveAsync(PersonId, submitted.SequenceNumber, submitted.Version + 1, StaffActor, CorrelationId, TestContext.Current.CancellationToken));

        Assert.Equal("concurrency_conflict", error.Code);
    }

    [Fact]
    public async Task sequence_numbers_increment_per_person_across_multiple_submissions()
    {
        var (service, _, _, _, _) = MakeService();

        var first = await service.SubmitAsync(PersonId, CompliantPrintContent(), StaffActor, CorrelationId, TestContext.Current.CancellationToken);
        var second = await service.SubmitAsync(PersonId, CompliantPrintContent(), StaffActor, CorrelationId, TestContext.Current.CancellationToken);

        Assert.Equal(1, first.SequenceNumber);
        Assert.Equal(2, second.SequenceNumber);
    }

    [Fact]
    public async Task an_unsupported_format_is_rejected_before_either_quality_gate_runs()
    {
        var (service, _, _, _, biometric) = MakeService();

        await Assert.ThrowsAsync<PhotoQualityError>(() =>
            service.SubmitAsync(PersonId, [0x00, 0x01, 0x02], StaffActor, CorrelationId, TestContext.Current.CancellationToken));

        Assert.Equal(0, biometric.CallCount);
    }

    [Fact]
    public async Task a_nonexistent_person_is_refused()
    {
        var repository = new InMemoryPhotoQualityCaptureRepository();
        var service = new PhotoQualityService(
            new FakeCurrentTenantProvider(TenantId), repository, new FakePersonExistenceGateway(new HashSet<string>()),
            new FakeBiometricSuitabilityGateway(), new FakePhotoMasterPromotionGateway(), new InMemoryPhotoQualityAuditSink(),
            new PhotoQualityCaptureLockRegistry(), new InMemoryIdempotencyStore());

        var error = await Assert.ThrowsAsync<PhotoQualityError>(() =>
            service.SubmitAsync(PersonId, CompliantPrintContent(), StaffActor, CorrelationId, TestContext.Current.CancellationToken));

        Assert.Equal("person_not_found", error.Code);
    }

    private static readonly AuditActor OtherOperator = new(ActorType.User, "a_completely_different_operator");

    [Fact]
    public async Task ZB_PHO003_02_a_crash_before_the_marker_is_ever_written_leaves_no_trace_and_a_fresh_attempt_proceeds_normally()
    {
        var innerRepository = new InMemoryPhotoQualityCaptureRepository();
        var repository = new FakeControllableCaptureRepository(innerRepository);
        var auditSink = new InMemoryPhotoQualityAuditSink();
        var promotion = new FakePhotoMasterPromotionGateway();
        var biometric = new FakeBiometricSuitabilityGateway();
        var service = new PhotoQualityService(
            new FakeCurrentTenantProvider(TenantId), repository, new FakePersonExistenceGateway(), biometric, promotion, auditSink,
            new PhotoQualityCaptureLockRegistry(), new InMemoryIdempotencyStore());
        var ct = TestContext.Current.CancellationToken;

        var submitted = await service.SubmitAsync(PersonId, CompliantPrintContent(), StaffActor, CorrelationId, ct);

        // The marker-commit write itself (ApproveAsync's FIRST ReplaceAsync call) fails -
        // PromoteAsync must never even be attempted.
        repository.FailOnReplaceCallNumber = 1;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ApproveAsync(PersonId, submitted.SequenceNumber, submitted.Version, StaffActor, CorrelationId, ct));

        Assert.Empty(promotion.Promotions);
        var afterFailure = await innerRepository.GetAsync(TenantId, PersonId, submitted.SequenceNumber, ct);
        Assert.Equal(PhotoQualityCaptureStatus.Pending, afterFailure!.Status);
        Assert.Null(afterFailure.PendingApprovalAttemptId); // no trace of the failed attempt survives

        // A fresh retry (repository healthy again) proceeds exactly like a normal first attempt.
        var approved = await service.ApproveAsync(PersonId, submitted.SequenceNumber, submitted.Version, StaffActor, CorrelationId, ct);
        Assert.Equal(PhotoQualityCaptureStatus.Approved, approved.Status);
        Assert.Single(promotion.Promotions);
    }

    [Fact]
    public async Task ZB_PHO003_04_a_crash_strictly_between_promotion_succeeding_and_the_local_claim_being_completed_fails_loud_on_retry_rather_than_reusing_wrong_data()
    {
        // Kemi's own named residual (not a new gap, disclosed not chased further): this is the
        // ONE window genuinely irreducible with a volatile, in-memory idempotency store - a
        // crash strictly between PromoteAsync succeeding and ApproveAsync's own Complete() call
        // leaves the claim stuck InFlight forever. The contract is that a retry FAILS LOUD
        // (bounded wait, then idempotency_wait_timeout) rather than silently reusing or
        // re-promoting - strictly better than today's silent wrong-photo risk.
        var repository = new InMemoryPhotoQualityCaptureRepository();
        var auditSink = new InMemoryPhotoQualityAuditSink();
        var promotion = new FakePhotoMasterPromotionGateway();
        var biometric = new FakeBiometricSuitabilityGateway();
        var idempotencyStore = new InMemoryIdempotencyStore();
        var service = new PhotoQualityService(
            new FakeCurrentTenantProvider(TenantId), repository, new FakePersonExistenceGateway(), biometric, promotion, auditSink,
            new PhotoQualityCaptureLockRegistry(), idempotencyStore,
            idempotencyWaitTimeout: TimeSpan.FromMilliseconds(100), idempotencyWaitPollInterval: TimeSpan.FromMilliseconds(10));
        var ct = TestContext.Current.CancellationToken;

        var content = CompliantPrintContent();
        var submitted = await service.SubmitAsync(PersonId, content, StaffActor, CorrelationId, ct);

        // Simulate the crash directly: durably record the SAME intent marker ApproveAsync's own
        // first branch would have written, claim the idempotency key (as the real owner would),
        // and let PromoteAsync genuinely succeed - then stop, exactly as if the process died
        // before ever reaching Complete().
        var attemptId = CanonicalId.NewEventId();
        var marked = new PhotoQualityCapture(
            PersonId, TenantId, submitted.SequenceNumber, submitted.Format, submitted.WidthPx, submitted.HeightPx, submitted.NativeDpi, submitted.Content,
            submitted.PrintQualityPassed, submitted.PrintQualityReasons, submitted.BiometricSuitabilityPassed, submitted.BiometricSuitabilityReasons,
            status: PhotoQualityCaptureStatus.Pending, decisionReason: null, promotedMasterVersion: null,
            version: submitted.Version, submittedAt: submitted.SubmittedAt, decidedAt: null,
            pendingApprovalAttemptId: attemptId, pendingApprovalActorId: StaffActor.Id, pendingApprovalActorType: StaffActor.Type);
        await repository.ReplaceAsync(submitted, marked, ct);
        idempotencyStore.Claim(TenantId, "approve_photo_quality_capture", attemptId, $"{PersonId}\u0000{submitted.SequenceNumber}");
        await promotion.PromoteAsync(PersonId, content, StaffActor, attemptId, ct); // "crashed" right here - Complete() never runs

        var error = await Assert.ThrowsAsync<PhotoQualityError>(() =>
            service.ApproveAsync(PersonId, submitted.SequenceNumber, submitted.Version, OtherOperator, CorrelationId, ct));

        Assert.Equal("idempotency_wait_timeout", error.Code);
        Assert.Single(promotion.Promotions); // never re-promoted while stuck - fails loud instead
    }

    [Fact]
    public async Task ZB_PHO003_02_a_failed_promotion_releases_its_claim_so_a_real_retry_can_genuinely_promote()
    {
        var repository = new InMemoryPhotoQualityCaptureRepository();
        var auditSink = new InMemoryPhotoQualityAuditSink();
        var promotion = new FakePhotoMasterPromotionGateway();
        var biometric = new FakeBiometricSuitabilityGateway();
        var service = new PhotoQualityService(
            new FakeCurrentTenantProvider(TenantId), repository, new FakePersonExistenceGateway(), biometric, promotion, auditSink,
            new PhotoQualityCaptureLockRegistry(), new InMemoryIdempotencyStore());
        var ct = TestContext.Current.CancellationToken;

        var submitted = await service.SubmitAsync(PersonId, CompliantPrintContent(), StaffActor, CorrelationId, ct);

        // The external promotion call itself genuinely fails (e.g. BIO-PHO-001's own repository
        // commit failing) - the claim must be released, not left stuck, so a real retry can
        // actually try again rather than waiting out a timeout for nothing.
        promotion.FailNextPromotion = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ApproveAsync(PersonId, submitted.SequenceNumber, submitted.Version, StaffActor, CorrelationId, ct));
        Assert.Empty(promotion.Promotions); // the failed attempt never actually promoted anything

        var approved = await service.ApproveAsync(PersonId, submitted.SequenceNumber, submitted.Version, StaffActor, CorrelationId, ct);

        Assert.Equal(PhotoQualityCaptureStatus.Approved, approved.Status);
        Assert.Equal(1, approved.PromotedMasterVersion);
        Assert.Single(promotion.Promotions); // exactly one REAL promotion, on the genuine retry
        var auditEvent = Assert.Single(auditSink.Events, e => e.Action == "photo_quality.capture.approve");
        Assert.Equal(StaffActor.Id, auditEvent.Actor.Id);
    }

    [Fact]
    public async Task ZB_PHO003_02_a_crash_after_promotion_but_before_the_audit_write_does_not_promote_twice_on_retry_and_credits_the_original_actor()
    {
        var repository = new InMemoryPhotoQualityCaptureRepository();
        var auditSink = new FakeControllableAuditSink { FailWrites = true };
        var promotion = new FakePhotoMasterPromotionGateway();
        var biometric = new FakeBiometricSuitabilityGateway();
        var service = new PhotoQualityService(
            new FakeCurrentTenantProvider(TenantId), repository, new FakePersonExistenceGateway(), biometric, promotion, auditSink,
            new PhotoQualityCaptureLockRegistry(), new InMemoryIdempotencyStore());
        var ct = TestContext.Current.CancellationToken;

        auditSink.FailWrites = false;
        var submitted = await service.SubmitAsync(PersonId, CompliantPrintContent(), StaffActor, CorrelationId, ct);

        auditSink.FailWrites = true;
        var error = await Assert.ThrowsAsync<PhotoQualityError>(() =>
            service.ApproveAsync(PersonId, submitted.SequenceNumber, submitted.Version, StaffActor, CorrelationId, ct));
        Assert.Equal("audit_unavailable", error.Code);
        Assert.Single(promotion.Promotions);

        // A different operator's retry finds the LOCAL idempotency claim already Completed and
        // returns its stored result directly - zero further calls to BIO-PHO-001 at all - and
        // is still attributed to the ORIGINAL initiator.
        auditSink.FailWrites = false;
        var approved = await service.ApproveAsync(PersonId, submitted.SequenceNumber, submitted.Version, OtherOperator, CorrelationId, ct);

        Assert.Equal(PhotoQualityCaptureStatus.Approved, approved.Status);
        Assert.Single(promotion.Promotions);
        var auditEvent = Assert.Single(auditSink.Events, e => e.Action == "photo_quality.capture.approve");
        Assert.Equal(StaffActor.Id, auditEvent.Actor.Id);
    }

    [Fact]
    public async Task ZB_PHO003_02_a_crash_after_the_audit_write_but_before_the_final_commit_does_not_repromote_or_reaudit()
    {
        var innerRepository = new InMemoryPhotoQualityCaptureRepository();
        var repository = new FakeControllableCaptureRepository(innerRepository);
        var auditSink = new InMemoryPhotoQualityAuditSink();
        var promotion = new FakePhotoMasterPromotionGateway();
        var biometric = new FakeBiometricSuitabilityGateway();
        var service = new PhotoQualityService(
            new FakeCurrentTenantProvider(TenantId), repository, new FakePersonExistenceGateway(), biometric, promotion, auditSink,
            new PhotoQualityCaptureLockRegistry(), new InMemoryIdempotencyStore());
        var ct = TestContext.Current.CancellationToken;

        var submitted = await service.SubmitAsync(PersonId, CompliantPrintContent(), StaffActor, CorrelationId, ct);

        // ReplaceAsync call #1 is the marker commit (succeeds); #2 is the FINAL Approved commit,
        // reached only after promotion AND audit have both already genuinely succeeded.
        repository.FailOnReplaceCallNumber = 2;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ApproveAsync(PersonId, submitted.SequenceNumber, submitted.Version, StaffActor, CorrelationId, ct));

        Assert.Single(promotion.Promotions);
        Assert.Single(auditSink.Events, e => e.Action == "photo_quality.capture.approve");

        var afterFailure = await innerRepository.GetAsync(TenantId, PersonId, submitted.SequenceNumber, ct);
        Assert.Equal(PhotoQualityCaptureStatus.Pending, afterFailure!.Status);

        var approved = await service.ApproveAsync(PersonId, submitted.SequenceNumber, submitted.Version, OtherOperator, CorrelationId, ct);

        Assert.Equal(PhotoQualityCaptureStatus.Approved, approved.Status);
        Assert.Single(promotion.Promotions); // never re-promoted
        Assert.Single(auditSink.Events, e => e.Action == "photo_quality.capture.approve"); // never re-audited (ExistsAsync skip)
    }

    [Fact]
    public async Task ZB_PHO003_02_rejecting_a_capture_whose_approval_is_already_in_progress_is_refused_not_a_silent_orphan()
    {
        var repository = new InMemoryPhotoQualityCaptureRepository();
        var auditSink = new FakeControllableAuditSink();
        var promotion = new FakePhotoMasterPromotionGateway();
        var biometric = new FakeBiometricSuitabilityGateway();
        var service = new PhotoQualityService(
            new FakeCurrentTenantProvider(TenantId), repository, new FakePersonExistenceGateway(), biometric, promotion, auditSink,
            new PhotoQualityCaptureLockRegistry(), new InMemoryIdempotencyStore());
        var ct = TestContext.Current.CancellationToken;

        var submitted = await service.SubmitAsync(PersonId, CompliantPrintContent(), StaffActor, CorrelationId, ct);
        auditSink.FailWrites = true;
        await Assert.ThrowsAsync<PhotoQualityError>(() =>
            service.ApproveAsync(PersonId, submitted.SequenceNumber, submitted.Version, StaffActor, CorrelationId, ct));

        var error = await Assert.ThrowsAsync<PhotoQualityError>(() =>
            service.RejectAsync(PersonId, submitted.SequenceNumber, "trying to reject instead", submitted.Version, StaffActor, CorrelationId, ct));

        Assert.Equal("approval_already_in_progress", error.Code);
        Assert.Single(promotion.Promotions); // the already-promoted master is never orphaned by a reject
    }

    [Fact]
    public async Task no_tenant_context_is_refused()
    {
        var (service, _, _, _, _) = MakeService(tenantId: null);

        var error = await Assert.ThrowsAsync<PhotoQualityError>(() =>
            service.SubmitAsync(PersonId, CompliantPrintContent(), StaffActor, CorrelationId, TestContext.Current.CancellationToken));

        Assert.Equal("tenant_context_missing", error.Code);
    }
}

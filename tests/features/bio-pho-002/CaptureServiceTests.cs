using Biolog.Features.PhotoBooth.Audit;
using Biolog.Features.PhotoBooth.Domain;
using Xunit;

namespace Biolog.Features.PhotoBooth.Tests;

public sealed class CaptureServiceTests
{
    private const string TenantId = "ten_018f2e5a-1234-7abc-89ab-0123456789ab";
    private const string PersonId = "per_018f2e5a-9c3d-7a41-9b2e-4f7c8d1a6e5b";
    private const string DeviceId = "dev_018f2e5a-9c3d-7a41-9b2e-4f7c8d1a6e5b";
    private static readonly AuditActor DeviceActor = new(ActorType.Device, DeviceId);
    private static readonly AuditActor ReviewerActor = new(ActorType.User, "reviewer_1");

    private static (CaptureService Service, IBoothCaptureRepository Repository, InMemoryCaptureAuditSink AuditSink, FakePhotoMasterPromotionGateway Promotion) MakeService(string? tenantId = TenantId)
    {
        var repository = new InMemoryBoothCaptureRepository();
        var auditSink = new InMemoryCaptureAuditSink();
        var promotion = new FakePhotoMasterPromotionGateway();
        var service = new CaptureService(
            new FakeCurrentTenantProvider(tenantId), repository, new FakePersonExistenceGateway(), promotion, auditSink,
            new InMemoryIdempotencyStore(), new BoothCaptureLockRegistry());
        return (service, repository, auditSink, promotion);
    }

    private static byte[] CompliantContent() => SyntheticImages.Png(CaptureSpecification.Current.FrameWidthPx, CaptureSpecification.Current.FrameHeightPx, CaptureSpecification.Current.MinimumDpi);

    [Fact]
    public async Task AC01_a_compliant_submission_is_recorded_pending_at_sequence_1()
    {
        var (service, _, _, _) = MakeService();

        var capture = await service.SubmitAsync(
            PersonId, DeviceId, CompliantContent(), true, true, "key-1", DeviceActor, "evt_018f2e5a-aaaa-7bbb-8ccc-000000000001", TestContext.Current.CancellationToken);

        Assert.Equal(1, capture.SequenceNumber);
        Assert.Equal(CaptureStatus.Pending, capture.Status);
    }

    [Fact]
    public async Task AC04_a_low_quality_submission_is_recorded_already_rejected_never_pending()
    {
        var (service, _, _, _) = MakeService();
        var tooSmall = SyntheticImages.Png(10, 10);

        var capture = await service.SubmitAsync(
            PersonId, DeviceId, tooSmall, true, true, "key-1", DeviceActor, "evt_018f2e5a-aaaa-7bbb-8ccc-000000000001", TestContext.Current.CancellationToken);

        Assert.Equal(CaptureStatus.Rejected, capture.Status);
        Assert.NotEmpty(capture.RejectionReasons);
        Assert.Null(capture.PromotedMasterVersion);
    }

    [Fact]
    public async Task AC03_replaying_the_same_idempotency_key_and_content_returns_the_same_capture_without_creating_a_second_one()
    {
        var (service, repository, _, _) = MakeService();
        var content = CompliantContent();

        var first = await service.SubmitAsync(PersonId, DeviceId, content, true, true, "key-1", DeviceActor, "evt_018f2e5a-aaaa-7bbb-8ccc-000000000001", TestContext.Current.CancellationToken);
        var replay = await service.SubmitAsync(PersonId, DeviceId, content, true, true, "key-1", DeviceActor, "evt_018f2e5a-aaaa-7bbb-8ccc-000000000002", TestContext.Current.CancellationToken);

        Assert.Equal(first.SequenceNumber, replay.SequenceNumber);
        var all = await repository.ListForPersonAsync(TenantId, PersonId, TestContext.Current.CancellationToken);
        Assert.Single(all);
    }

    [Fact]
    public async Task AC03_reusing_an_idempotency_key_with_different_content_is_refused()
    {
        var (service, _, _, _) = MakeService();
        await service.SubmitAsync(PersonId, DeviceId, CompliantContent(), true, true, "key-1", DeviceActor, "evt_018f2e5a-aaaa-7bbb-8ccc-000000000001", TestContext.Current.CancellationToken);

        var error = await Assert.ThrowsAsync<PhotoBoothError>(() =>
            service.SubmitAsync(PersonId, DeviceId, SyntheticImages.Jpeg(1050, 1350), true, true, "key-1", DeviceActor, "evt_018f2e5a-aaaa-7bbb-8ccc-000000000002", TestContext.Current.CancellationToken));
        Assert.Equal("idempotency_key_reuse", error.Code);
    }

    [Fact]
    public async Task AC03_a_rejected_submissions_idempotency_key_replays_the_same_rejection_not_a_re_evaluation()
    {
        var (service, _, _, _) = MakeService();
        var tooSmall = SyntheticImages.Png(10, 10);

        var first = await service.SubmitAsync(PersonId, DeviceId, tooSmall, true, true, "key-1", DeviceActor, "evt_018f2e5a-aaaa-7bbb-8ccc-000000000001", TestContext.Current.CancellationToken);
        var replay = await service.SubmitAsync(PersonId, DeviceId, tooSmall, true, true, "key-1", DeviceActor, "evt_018f2e5a-aaaa-7bbb-8ccc-000000000002", TestContext.Current.CancellationToken);

        Assert.Equal(first.SequenceNumber, replay.SequenceNumber);
        Assert.Equal(CaptureStatus.Rejected, replay.Status);
    }

    [Fact]
    public async Task AC04_approving_a_pending_capture_promotes_it_and_records_the_master_version()
    {
        var (service, _, _, promotion) = MakeService();
        var capture = await service.SubmitAsync(PersonId, DeviceId, CompliantContent(), true, true, "key-1", DeviceActor, "evt_018f2e5a-aaaa-7bbb-8ccc-000000000001", TestContext.Current.CancellationToken);

        var approved = await service.ApproveAsync(PersonId, capture.SequenceNumber, capture.Version, ReviewerActor, "evt_018f2e5a-aaaa-7bbb-8ccc-000000000002", TestContext.Current.CancellationToken);

        Assert.Equal(CaptureStatus.Promoted, approved.Status);
        Assert.NotNull(approved.PromotedMasterVersion);
        Assert.Single(promotion.Promotions);
        Assert.Equal(PersonId, promotion.Promotions[0].PersonId);
    }

    [Fact]
    public async Task AC04_a_rejected_capture_can_never_be_approved()
    {
        var (service, _, _, promotion) = MakeService();
        var tooSmall = SyntheticImages.Png(10, 10);
        var capture = await service.SubmitAsync(PersonId, DeviceId, tooSmall, true, true, "key-1", DeviceActor, "evt_018f2e5a-aaaa-7bbb-8ccc-000000000001", TestContext.Current.CancellationToken);

        var error = await Assert.ThrowsAsync<PhotoBoothError>(() =>
            service.ApproveAsync(PersonId, capture.SequenceNumber, capture.Version, ReviewerActor, "evt_018f2e5a-aaaa-7bbb-8ccc-000000000002", TestContext.Current.CancellationToken));

        Assert.Equal("capture_not_pending", error.Code);
        Assert.Empty(promotion.Promotions);
    }

    [Fact]
    public async Task AC04_a_promoted_capture_can_never_be_approved_or_rejected_again()
    {
        var (service, _, _, _) = MakeService();
        var capture = await service.SubmitAsync(PersonId, DeviceId, CompliantContent(), true, true, "key-1", DeviceActor, "evt_018f2e5a-aaaa-7bbb-8ccc-000000000001", TestContext.Current.CancellationToken);
        var approved = await service.ApproveAsync(PersonId, capture.SequenceNumber, capture.Version, ReviewerActor, "evt_018f2e5a-aaaa-7bbb-8ccc-000000000002", TestContext.Current.CancellationToken);

        var error = await Assert.ThrowsAsync<PhotoBoothError>(() =>
            service.ApproveAsync(PersonId, capture.SequenceNumber, approved.Version, ReviewerActor, "evt_018f2e5a-aaaa-7bbb-8ccc-000000000003", TestContext.Current.CancellationToken));
        Assert.Equal("capture_not_pending", error.Code);
    }

    [Fact]
    public async Task Rejecting_a_pending_capture_requires_a_reason()
    {
        var (service, _, _, _) = MakeService();
        var capture = await service.SubmitAsync(PersonId, DeviceId, CompliantContent(), true, true, "key-1", DeviceActor, "evt_018f2e5a-aaaa-7bbb-8ccc-000000000001", TestContext.Current.CancellationToken);

        var error = await Assert.ThrowsAsync<PhotoBoothError>(() =>
            service.RejectAsync(PersonId, capture.SequenceNumber, "", capture.Version, ReviewerActor, "evt_018f2e5a-aaaa-7bbb-8ccc-000000000002", TestContext.Current.CancellationToken));
        Assert.Equal("validation_failed", error.Code);
    }

    [Fact]
    public async Task Rejecting_a_pending_capture_with_a_stale_expected_version_is_a_concurrency_conflict()
    {
        var (service, _, _, _) = MakeService();
        var capture = await service.SubmitAsync(PersonId, DeviceId, CompliantContent(), true, true, "key-1", DeviceActor, "evt_018f2e5a-aaaa-7bbb-8ccc-000000000001", TestContext.Current.CancellationToken);

        var error = await Assert.ThrowsAsync<PhotoBoothError>(() =>
            service.RejectAsync(PersonId, capture.SequenceNumber, "bad photo", 999, ReviewerActor, "evt_018f2e5a-aaaa-7bbb-8ccc-000000000002", TestContext.Current.CancellationToken));
        Assert.Equal("concurrency_conflict", error.Code);
    }

    [Fact]
    public async Task Fails_closed_when_no_tenant_context_has_resolved()
    {
        var (service, _, _, _) = MakeService(tenantId: null);

        var error = await Assert.ThrowsAsync<PhotoBoothError>(() =>
            service.SubmitAsync(PersonId, DeviceId, CompliantContent(), true, true, "key-1", DeviceActor, "evt_018f2e5a-aaaa-7bbb-8ccc-000000000001", TestContext.Current.CancellationToken));
        Assert.Equal("tenant_context_missing", error.Code);
    }

    [Fact]
    public async Task Rejects_a_person_that_does_not_exist_in_the_resolved_tenant()
    {
        var repository = new InMemoryBoothCaptureRepository();
        var service = new CaptureService(
            new FakeCurrentTenantProvider(TenantId), repository, new FakePersonExistenceGateway(new HashSet<string>()),
            new FakePhotoMasterPromotionGateway(), new InMemoryCaptureAuditSink(), new InMemoryIdempotencyStore(), new BoothCaptureLockRegistry());

        var error = await Assert.ThrowsAsync<PhotoBoothError>(() =>
            service.SubmitAsync(PersonId, DeviceId, CompliantContent(), true, true, "key-1", DeviceActor, "evt_018f2e5a-aaaa-7bbb-8ccc-000000000001", TestContext.Current.CancellationToken));
        Assert.Equal("person_not_found", error.Code);
    }

    [Fact]
    public async Task AC01_two_submissions_for_the_same_person_get_sequential_sequence_numbers()
    {
        var (service, _, _, _) = MakeService();

        var first = await service.SubmitAsync(PersonId, DeviceId, CompliantContent(), true, true, "key-1", DeviceActor, "evt_018f2e5a-aaaa-7bbb-8ccc-000000000001", TestContext.Current.CancellationToken);
        var second = await service.SubmitAsync(PersonId, DeviceId, CompliantContent(), true, true, "key-2", DeviceActor, "evt_018f2e5a-aaaa-7bbb-8ccc-000000000002", TestContext.Current.CancellationToken);

        Assert.Equal(1, first.SequenceNumber);
        Assert.Equal(2, second.SequenceNumber);
    }

    [Fact]
    public async Task AC04_the_audit_trail_never_carries_the_raw_capture_content()
    {
        var (service, _, auditSink, _) = MakeService();
        await service.SubmitAsync(PersonId, DeviceId, CompliantContent(), true, true, "key-1", DeviceActor, "evt_018f2e5a-aaaa-7bbb-8ccc-000000000001", TestContext.Current.CancellationToken);

        Assert.All(auditSink.Events, e => Assert.DoesNotContain(e.Detail.Values, v => v is byte[]));
    }
}

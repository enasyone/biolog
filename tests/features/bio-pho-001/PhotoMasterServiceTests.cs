using Biolog.Features.Photography.Audit;
using Biolog.Features.Photography.Domain;
using Xunit;

namespace Biolog.Features.Photography.Tests;

public sealed class PhotoMasterServiceTests
{
    private const string TenantId = "ten_018f2e5a-1234-7abc-89ab-0123456789ab";
    private const string PersonId = "per_018f2e5a-9c3d-7a41-9b2e-4f7c8d1a6e5b";
    private static readonly AuditActor Actor = new(ActorType.User, "actor_1");
    private const string CorrelationId = "evt_018f2e5a-aaaa-7bbb-8ccc-000000000001";

    private static PhotoMasterService MakeService(
        out IPhotoMasterRepository repository, out InMemoryPhotoAuditSink auditSink, string? tenantId = TenantId)
    {
        repository = new InMemoryPhotoMasterRepository();
        auditSink = new InMemoryPhotoAuditSink();
        return new PhotoMasterService(
            new FakeCurrentTenantProvider(tenantId), repository, new FakePersonExistenceGateway(), auditSink, new PhotoMasterLockRegistry());
    }

    [Fact]
    public async Task AC01_the_first_appended_master_starts_at_version_1()
    {
        var service = MakeService(out _, out _);

        var master = await service.AppendAsync(PersonId, SyntheticImages.Png(300, 200), Actor, CorrelationId, TestContext.Current.CancellationToken);

        Assert.Equal(1, master.Version);
        Assert.Equal(PhotoFormat.Png, master.Format);
    }

    [Fact]
    public async Task AC01_a_second_append_for_the_same_person_creates_version_2_and_leaves_version_1_untouched()
    {
        var service = MakeService(out var repository, out _);

        var first = await service.AppendAsync(PersonId, SyntheticImages.Png(300, 200), Actor, CorrelationId, TestContext.Current.CancellationToken);
        var second = await service.AppendAsync(PersonId, SyntheticImages.Jpeg(640, 480), Actor, "evt_018f2e5a-aaaa-7bbb-8ccc-000000000002", TestContext.Current.CancellationToken);

        Assert.Equal(1, first.Version);
        Assert.Equal(2, second.Version);

        var reloadedFirst = await repository.GetVersionAsync(TenantId, PersonId, 1, TestContext.Current.CancellationToken);
        Assert.NotNull(reloadedFirst);
        Assert.Equal(PhotoFormat.Png, reloadedFirst!.Format);
        Assert.Equal(300, reloadedFirst.WidthPx); // unchanged by the second append
    }

    [Fact]
    public async Task AC01_appending_10_times_concurrently_for_the_same_person_never_produces_a_duplicate_or_skipped_version()
    {
        var service = MakeService(out var repository, out _);

        var tasks = Enumerable.Range(0, 10).Select(i => service.AppendAsync(
            PersonId, SyntheticImages.Png(100 + i, 100 + i), Actor, $"evt_018f2e5a-aaaa-7bbb-8ccc-{i:D12}", TestContext.Current.CancellationToken));
        await Task.WhenAll(tasks);

        var versions = (await repository.ListVersionsAsync(TenantId, PersonId, TestContext.Current.CancellationToken))
            .Select(m => m.Version).OrderBy(v => v).ToList();
        Assert.Equal(Enumerable.Range(1, 10), versions);
    }

    [Fact]
    public async Task Rejects_a_person_that_does_not_exist_in_the_resolved_tenant()
    {
        var repository = new InMemoryPhotoMasterRepository();
        var auditSink = new InMemoryPhotoAuditSink();
        var service = new PhotoMasterService(
            new FakeCurrentTenantProvider(TenantId), repository, new FakePersonExistenceGateway(new HashSet<string>()), auditSink, new PhotoMasterLockRegistry());

        var error = await Assert.ThrowsAsync<PhotoError>(() =>
            service.AppendAsync(PersonId, SyntheticImages.Png(10, 10), Actor, CorrelationId, TestContext.Current.CancellationToken));
        Assert.Equal("person_not_found", error.Code);
    }

    [Fact]
    public async Task Rejects_content_that_is_not_a_supported_format()
    {
        var service = MakeService(out _, out _);

        var error = await Assert.ThrowsAsync<PhotoError>(() =>
            service.AppendAsync(PersonId, [0x00, 0x01, 0x02], Actor, CorrelationId, TestContext.Current.CancellationToken));
        Assert.Equal("unsupported_format", error.Code);
    }

    [Fact]
    public async Task Fails_closed_when_no_tenant_context_has_resolved()
    {
        var service = MakeService(out _, out _, tenantId: null);

        var error = await Assert.ThrowsAsync<PhotoError>(() =>
            service.AppendAsync(PersonId, SyntheticImages.Png(10, 10), Actor, CorrelationId, TestContext.Current.CancellationToken));
        Assert.Equal("tenant_context_missing", error.Code);
    }

    [Fact]
    public async Task AC04_appending_a_master_records_an_audit_event_naming_the_version_but_never_the_photo_bytes()
    {
        var service = MakeService(out _, out var auditSink);

        await service.AppendAsync(PersonId, SyntheticImages.Png(300, 200), Actor, CorrelationId, TestContext.Current.CancellationToken);

        var recorded = Assert.Single(auditSink.Events);
        Assert.Equal("photo.master.append", recorded.Action);
        Assert.Equal("photo_master", recorded.Target.Type);
        Assert.Equal(PersonId, recorded.Target.Id);
        Assert.Equal(1, recorded.Detail["version"]);
        Assert.DoesNotContain(recorded.Detail.Values, v => v is byte[]); // the raw content is never carried
    }
}

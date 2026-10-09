using Biolog.Features.Photography.Audit;
using Biolog.Features.Photography.Domain;
using Xunit;

namespace Biolog.Features.Photography.Tests;

public sealed class PhotoDerivativeServiceTests
{
    private const string TenantId = "ten_018f2e5a-1234-7abc-89ab-0123456789ab";
    private const string PersonId = "per_018f2e5a-9c3d-7a41-9b2e-4f7c8d1a6e5b";
    private static readonly AuditActor Actor = new(ActorType.User, "actor_1");

    private static (PhotoMasterService Masters, PhotoDerivativeService Derivatives, IPhotoMasterRepository MasterRepo) MakeServices()
    {
        var masterRepo = new InMemoryPhotoMasterRepository();
        var derivativeRepo = new InMemoryPhotoDerivativeRepository();
        var lockRegistry = new PhotoMasterLockRegistry();
        var tenantProvider = new FakeCurrentTenantProvider(TenantId);

        var masters = new PhotoMasterService(
            tenantProvider, masterRepo, new FakePersonExistenceGateway(), new InMemoryPhotoAuditSink(), lockRegistry);
        var derivatives = new PhotoDerivativeService(
            tenantProvider, masterRepo, derivativeRepo, new MetadataOnlyPhotoDerivativeGenerator(), new InMemoryPhotoAuditSink(), lockRegistry);

        return (masters, derivatives, masterRepo);
    }

    [Fact]
    public async Task AC02_a_regenerated_derivative_references_its_masters_version()
    {
        var (masters, derivatives, _) = MakeServices();
        var ct = TestContext.Current.CancellationToken;
        var master = await masters.AppendAsync(PersonId, SyntheticImages.Png(300, 200), Actor, "evt_018f2e5a-aaaa-7bbb-8ccc-000000000001", ct);

        var derivative = await derivatives.RegenerateAsync(PersonId, master.Version, DerivativeKind.Print, Actor, "evt_018f2e5a-aaaa-7bbb-8ccc-000000000002", ct);

        Assert.Equal(master.Version, derivative.MasterVersion);
        Assert.Equal(DerivativeKind.Print, derivative.Kind);
    }

    [Fact]
    public async Task AC03_regenerating_a_derivative_many_times_never_changes_the_masters_own_content_or_version()
    {
        var (masters, derivatives, masterRepo) = MakeServices();
        var ct = TestContext.Current.CancellationToken;
        var originalContent = SyntheticImages.Png(300, 200);
        var master = await masters.AppendAsync(PersonId, originalContent, Actor, "evt_018f2e5a-aaaa-7bbb-8ccc-000000000001", ct);

        for (var i = 0; i < 5; i++)
        {
            await derivatives.RegenerateAsync(PersonId, master.Version, DerivativeKind.Print, Actor, $"evt_018f2e5a-aaaa-7bbb-8ccc-{i + 10:D12}", ct);
            await derivatives.RegenerateAsync(PersonId, master.Version, DerivativeKind.Display, Actor, $"evt_018f2e5a-aaaa-7bbb-8ccc-{i + 20:D12}", ct);
        }

        var reloadedMaster = await masterRepo.GetVersionAsync(TenantId, PersonId, master.Version, ct);
        Assert.NotNull(reloadedMaster);
        Assert.Equal(originalContent, reloadedMaster!.Content);
        Assert.Equal(1, reloadedMaster.Version);

        var versions = await masterRepo.ListVersionsAsync(TenantId, PersonId, ct);
        Assert.Single(versions); // regeneration never appended a second master version either
    }

    [Fact]
    public async Task AC03_regenerating_replaces_only_the_previous_derivative_of_the_same_kind()
    {
        var (masters, derivatives, _) = MakeServices();
        var ct = TestContext.Current.CancellationToken;
        var master = await masters.AppendAsync(PersonId, SyntheticImages.Png(300, 200), Actor, "evt_018f2e5a-aaaa-7bbb-8ccc-000000000001", ct);

        var first = await derivatives.RegenerateAsync(PersonId, master.Version, DerivativeKind.Print, Actor, "evt_018f2e5a-aaaa-7bbb-8ccc-000000000002", ct);
        var second = await derivatives.RegenerateAsync(PersonId, master.Version, DerivativeKind.Print, Actor, "evt_018f2e5a-aaaa-7bbb-8ccc-000000000003", ct);

        // Both come back with the same identity (person, master version, kind) - the second
        // call replaced the first's row rather than appending a sibling.
        Assert.Equal(first.PersonId, second.PersonId);
        var all = await derivatives.ListForMasterAsync(PersonId, master.Version, ct);
        Assert.Single(all);
    }

    [Fact]
    public async Task Regenerating_against_a_master_version_that_does_not_exist_is_rejected()
    {
        var (_, derivatives, _) = MakeServices();

        var error = await Assert.ThrowsAsync<PhotoError>(() =>
            derivatives.RegenerateAsync(PersonId, 999, DerivativeKind.Print, Actor, "evt_018f2e5a-aaaa-7bbb-8ccc-000000000001", TestContext.Current.CancellationToken));
        Assert.Equal("photo_master_not_found", error.Code);
    }
}

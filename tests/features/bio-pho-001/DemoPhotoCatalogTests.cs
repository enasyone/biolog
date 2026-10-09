using Biolog.Features.Photography.Domain;
using Xunit;

namespace Biolog.Features.Photography.Tests;

/// <summary>BIO-PHO-001-AC05 - see <see cref="DemoPhotoCatalog"/>'s own header for the property this proves.</summary>
public sealed class DemoPhotoCatalogTests
{
    private const string TenantId = "ten_018f2e5a-1234-7abc-89ab-0123456789ab";
    private const string PersonId = "per_018f2e5a-9c3d-7a41-9b2e-4f7c8d1a6e5b";

    [Fact]
    public void AC05_the_same_person_id_always_produces_byte_identical_synthetic_content()
    {
        var first = DemoPhotoCatalog.SyntheticContentFor(PersonId);
        var second = DemoPhotoCatalog.SyntheticContentFor(PersonId);

        Assert.Equal(first, second);
    }

    [Fact]
    public void AC05_different_person_ids_get_different_deterministic_content()
    {
        var a = DemoPhotoCatalog.SyntheticContentFor("per_018f2e5a-0000-7000-8000-000000000001");
        var b = DemoPhotoCatalog.SyntheticContentFor("per_018f2e5a-0000-7000-8000-000000000002");

        Assert.NotEqual(a, b);
    }

    [Fact]
    public async Task AC05_seeding_the_same_person_repeatedly_never_creates_a_second_master_version_or_changes_the_assignment()
    {
        var masterRepo = new InMemoryPhotoMasterRepository();
        var assignmentRepo = new InMemoryPhotoAssignmentRepository();
        var tenantProvider = new FakeCurrentTenantProvider(TenantId);
        var masters = new PhotoMasterService(
            tenantProvider, masterRepo, new FakePersonExistenceGateway(), new Audit.InMemoryPhotoAuditSink(), new PhotoMasterLockRegistry());
        var assignments = new PhotoAssignmentService(
            tenantProvider, assignmentRepo, masterRepo, new FakePersonExistenceGateway(), new Audit.InMemoryPhotoAuditSink(), new PhotoAssignmentLockRegistry());
        var ct = TestContext.Current.CancellationToken;

        var first = await DemoPhotoCatalog.SeedIdempotentlyAsync(masters, assignments, PersonId, ct);
        var second = await DemoPhotoCatalog.SeedIdempotentlyAsync(masters, assignments, PersonId, ct);
        var third = await DemoPhotoCatalog.SeedIdempotentlyAsync(masters, assignments, PersonId, ct);

        Assert.Equal(first.Version, second.Version);
        Assert.Equal(first.Version, third.Version);
        Assert.Equal(first.CurrentMasterVersion, second.CurrentMasterVersion);
        Assert.Equal(first.CurrentMasterVersion, third.CurrentMasterVersion);

        var versions = await masterRepo.ListVersionsAsync(TenantId, PersonId, ct);
        Assert.Single(versions); // three seed calls, exactly one master ever created
    }
}

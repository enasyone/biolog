// Developer tests for BIO-CRD-004. SYNTHETIC fixtures only - no real signatures/biometrics.
using Biolog.Features.CardObjects.Domain;
using Xunit;

namespace Biolog.Features.CardObjects.Tests;

public sealed class SignatureAssetRepositoryTests
{
    private const string TenantId = "ten_018f2e5a-1234-7abc-89ab-0123456789ab";
    private const string OtherTenantId = "ten_018f2e5a-1234-7abc-89ab-0123456789ac";

    [Fact]
    public void AC04_the_first_appended_version_for_a_lineage_is_1()
    {
        var repo = new InMemorySignatureAssetRepository();
        var asset = repo.AppendVersion(TenantId, "lineage-1", [1, 2, 3], "2026-09-22T00:00:00.000Z");
        Assert.Equal(1, asset.Version);
    }

    [Fact]
    public void AC04_each_append_increments_the_version_and_never_overwrites_the_previous_one()
    {
        var repo = new InMemorySignatureAssetRepository();
        var v1 = repo.AppendVersion(TenantId, "lineage-1", [1], "2026-09-22T00:00:00.000Z");
        var v2 = repo.AppendVersion(TenantId, "lineage-1", [2], "2026-09-22T00:00:01.000Z");
        var v3 = repo.AppendVersion(TenantId, "lineage-1", [3], "2026-09-22T00:00:02.000Z");

        Assert.Equal([1, 2, 3], new[] { v1.Version, v2.Version, v3.Version });

        // The original v1 row is still readable exactly as it was - append-only, never mutated.
        var reread = repo.GetVersion(TenantId, "lineage-1", 1);
        Assert.NotNull(reread);
        Assert.Equal([1], reread!.Content);
    }

    [Fact]
    public void AC04_get_latest_returns_the_highest_version()
    {
        var repo = new InMemorySignatureAssetRepository();
        repo.AppendVersion(TenantId, "lineage-1", [1], "2026-09-22T00:00:00.000Z");
        repo.AppendVersion(TenantId, "lineage-1", [2], "2026-09-22T00:00:01.000Z");
        var latest = repo.AppendVersion(TenantId, "lineage-1", [3], "2026-09-22T00:00:02.000Z");

        Assert.Equal(latest.Version, repo.GetLatestVersion(TenantId, "lineage-1")!.Version);
    }

    [Fact]
    public void AC04_list_versions_returns_every_version_in_ascending_order()
    {
        var repo = new InMemorySignatureAssetRepository();
        repo.AppendVersion(TenantId, "lineage-1", [1], "2026-09-22T00:00:00.000Z");
        repo.AppendVersion(TenantId, "lineage-1", [2], "2026-09-22T00:00:01.000Z");
        repo.AppendVersion(TenantId, "lineage-1", [3], "2026-09-22T00:00:02.000Z");

        var versions = repo.ListVersions(TenantId, "lineage-1").Select(a => a.Version).ToList();
        Assert.Equal([1, 2, 3], versions);
    }

    [Fact]
    public void A_lineage_in_one_tenant_is_invisible_to_another_tenant()
    {
        var repo = new InMemorySignatureAssetRepository();
        repo.AppendVersion(TenantId, "shared-lineage-id", [1], "2026-09-22T00:00:00.000Z");

        Assert.Null(repo.GetLatestVersion(OtherTenantId, "shared-lineage-id"));
        Assert.Empty(repo.ListVersions(OtherTenantId, "shared-lineage-id"));
    }

    [Fact]
    public void Two_tenants_may_independently_version_the_same_lineage_id_without_interfering()
    {
        var repo = new InMemorySignatureAssetRepository();
        var a1 = repo.AppendVersion(TenantId, "shared-lineage-id", [1], "2026-09-22T00:00:00.000Z");
        var b1 = repo.AppendVersion(OtherTenantId, "shared-lineage-id", [9], "2026-09-22T00:00:00.000Z");

        Assert.Equal(1, a1.Version);
        Assert.Equal(1, b1.Version); // independent sequence per tenant, not a shared counter
        Assert.Equal([1], repo.GetLatestVersion(TenantId, "shared-lineage-id")!.Content);
        Assert.Equal([9], repo.GetLatestVersion(OtherTenantId, "shared-lineage-id")!.Content);
    }

    [Fact]
    public void Concurrent_appends_to_the_same_lineage_never_collide_or_skip_a_version()
    {
        var repo = new InMemorySignatureAssetRepository();
        const int concurrentAppends = 50;

        Parallel.For(0, concurrentAppends, i =>
            repo.AppendVersion(TenantId, "race-lineage", [(byte)i], "2026-09-22T00:00:00.000Z"));

        var versions = repo.ListVersions(TenantId, "race-lineage").Select(a => a.Version).OrderBy(v => v).ToList();
        Assert.Equal(Enumerable.Range(1, concurrentAppends).ToList(), versions);
    }

    [Fact]
    public void Content_is_defensively_copied_so_a_caller_cannot_corrupt_a_stored_row()
    {
        var repo = new InMemorySignatureAssetRepository();
        var original = new byte[] { 1, 2, 3 };
        var asset = repo.AppendVersion(TenantId, "lineage-1", original, "2026-09-22T00:00:00.000Z");

        original[0] = 99; // mutate the caller's own array after the append
        Assert.Equal(1, asset.Content[0]); // stored row is unaffected

        var readBack = asset.Content;
        readBack[0] = 42; // mutate the array handed back by a read
        Assert.Equal(1, asset.Content[0]); // the NEXT read is still unaffected
    }
}

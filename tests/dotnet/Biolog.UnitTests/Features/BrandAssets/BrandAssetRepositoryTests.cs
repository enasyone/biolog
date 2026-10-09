// Developer tests for BIO-CRD-001-AC02/AC05. SYNTHETIC fixtures only.
using Biolog.Api.Features.BrandAssets;
using Xunit;

namespace Biolog.UnitTests.Features.BrandAssets;

public sealed class BrandAssetRepositoryTests
{
    private static BrandAsset MakeAsset(string tenantId, string id, string lineageId, int version, DateTimeOffset? createdAt = null) => new()
    {
        Id = id,
        LineageId = lineageId,
        Version = version,
        TenantId = tenantId,
        AssetType = BrandAssetType.PrimaryCrest,
        Status = BrandAssetStatus.Pending,
        Format = BrandAssetFormat.Png,
        FileName = "crest.png",
        ContentType = "image/png",
        SizeBytes = 10,
        Content = [1, 2, 3],
        ContentValidation = new ContentValidationResult(true, []),
        MalwareScan = new MalwareScanResult(MalwareScanVerdict.Clean, "test", []),
        UploadedBy = "tester",
        CreatedAtUtc = createdAt ?? DateTimeOffset.UtcNow,
    };

    [Fact]
    public void GetById_returns_null_for_a_different_tenant_not_403_shaped_leak()
    {
        var repo = new InMemoryBrandAssetRepository();
        repo.Add(MakeAsset("ten_a", "asset_1", "lineage_1", 1));

        Assert.Null(repo.GetById("ten_b", "asset_1"));
        Assert.NotNull(repo.GetById("ten_a", "asset_1"));
    }

    [Fact]
    public void GetById_returns_null_for_an_unknown_id()
    {
        var repo = new InMemoryBrandAssetRepository();
        Assert.Null(repo.GetById("ten_a", "does-not-exist"));
    }

    [Fact]
    public void List_never_returns_another_tenants_assets()
    {
        var repo = new InMemoryBrandAssetRepository();
        repo.Add(MakeAsset("ten_a", "asset_1", "lineage_1", 1));
        repo.Add(MakeAsset("ten_b", "asset_2", "lineage_2", 1));

        var page = repo.List("ten_a", new BrandAssetListFilter(null, null, null, null), 50, null);

        Assert.Single(page.Items);
        Assert.Equal("asset_1", page.Items[0].Id);
    }

    [Fact]
    public void List_paginates_with_an_opaque_cursor()
    {
        var repo = new InMemoryBrandAssetRepository();
        var baseline = DateTimeOffset.UtcNow;
        for (var i = 0; i < 5; i++)
        {
            repo.Add(MakeAsset("ten_a", $"asset_{i}", $"lineage_{i}", 1, baseline.AddSeconds(i)));
        }

        var firstPage = repo.List("ten_a", new BrandAssetListFilter(null, null, null, null), 2, null);
        Assert.Equal(2, firstPage.Items.Count);
        Assert.NotNull(firstPage.NextCursor);

        var secondPage = repo.List("ten_a", new BrandAssetListFilter(null, null, null, null), 2, firstPage.NextCursor);
        Assert.Equal(2, secondPage.Items.Count);
        Assert.DoesNotContain(secondPage.Items, item => firstPage.Items.Any(f => f.Id == item.Id));

        var thirdPage = repo.List("ten_a", new BrandAssetListFilter(null, null, null, null), 2, secondPage.NextCursor);
        Assert.Single(thirdPage.Items);
        Assert.Null(thirdPage.NextCursor);
    }

    [Fact]
    public void GetLatestVersion_ignores_other_tenants_lineages()
    {
        var repo = new InMemoryBrandAssetRepository();
        repo.Add(MakeAsset("ten_a", "asset_1", "lineage_1", 1));
        repo.Add(MakeAsset("ten_b", "asset_2", "lineage_1", 5)); // same lineage id, different tenant

        var latest = repo.GetLatestVersion("ten_a", "lineage_1");

        Assert.NotNull(latest);
        Assert.Equal(1, latest!.Version);
    }

    [Fact]
    public void UpdateStatus_returns_null_for_a_different_tenant()
    {
        var repo = new InMemoryBrandAssetRepository();
        repo.Add(MakeAsset("ten_a", "asset_1", "lineage_1", 1));

        var result = repo.UpdateStatus("ten_b", "asset_1", BrandAssetStatus.Approved);

        Assert.Null(result);
        Assert.Equal(BrandAssetStatus.Pending, repo.GetById("ten_a", "asset_1")!.Status);
    }

    [Fact]
    public void UpdateStatus_updates_the_record_for_the_owning_tenant()
    {
        var repo = new InMemoryBrandAssetRepository();
        repo.Add(MakeAsset("ten_a", "asset_1", "lineage_1", 1));

        var result = repo.UpdateStatus("ten_a", "asset_1", BrandAssetStatus.Approved);

        Assert.NotNull(result);
        Assert.Equal(BrandAssetStatus.Approved, result!.Status);
    }
}

using Biolog.Features.Biometrics.Domain;
using Biolog.Features.FingerprintSearch.Domain;
using Biolog.Features.FingerprintSearch.Service;
using Xunit;

namespace Biolog.Features.FingerprintSearch.Tests;

/// <summary>BIO-IDN-001-QA-01-shaped governance checks, mirroring every prior BIO-BIO story's
/// own.</summary>
public sealed class FingerprintSearchGovernanceTests
{
    [Fact]
    public void IFingerprintGalleryStore_and_InMemory_implementation_are_not_public()
    {
        Assert.False(typeof(IFingerprintGalleryStore).IsPublic);
        Assert.False(typeof(InMemoryFingerprintGalleryStore).IsPublic);
    }

    [Fact]
    public void IFingerprintDuplicateReviewRepository_and_InMemory_implementation_are_not_public()
    {
        Assert.False(typeof(IFingerprintDuplicateReviewRepository).IsPublic);
        Assert.False(typeof(InMemoryFingerprintDuplicateReviewRepository).IsPublic);
    }

    [Fact]
    public void FingerprintSearchService_is_not_public()
    {
        Assert.False(typeof(FingerprintSearchService).IsPublic);
    }

    [Fact]
    public void FingerprintDuplicateReviewEntry_has_no_public_constructor()
    {
        Assert.DoesNotContain(typeof(FingerprintDuplicateReviewEntry).GetConstructors(), c => c.IsPublic);
    }

    [Fact]
    public async Task GalleryStore_cross_tenant_lookup_never_returns_another_tenants_templates()
    {
        var store = new InMemoryFingerprintGalleryStore();
        var template = BiometricTemplate.Create(
            "t1", "ten_a", "per_a", BiometricModality.Fingerprint, "prov", "1.0", [1], "2026-01-01T00:00:00Z");
        await store.IndexAsync(template, CancellationToken.None);

        var tenantB = await store.GetAllForTenantAsync("ten_b", CancellationToken.None);

        Assert.Empty(tenantB);
    }

    [Fact]
    public async Task ReviewRepository_cross_tenant_lookup_returns_null_not_an_exception()
    {
        var repository = new InMemoryFingerprintDuplicateReviewRepository();
        var entry = new FingerprintDuplicateReviewEntry("ten_a", "per_a", "per_b")
        {
            Score = 1.0, Status = FingerprintDuplicateReviewStatus.PendingReview, Version = 1,
            CreatedAtUtc = "2026-01-01T00:00:00Z", UpdatedAtUtc = "2026-01-01T00:00:00Z",
        };
        await repository.CreateAsync(entry, CancellationToken.None);

        var result = await repository.GetAsync("ten_b", "per_a", "per_b", CancellationToken.None);

        Assert.Null(result);
    }
}

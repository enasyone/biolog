using Biolog.Features.FaceVerification.Domain;
using Biolog.Features.FaceVerification.Service;
using Xunit;

namespace Biolog.Features.FaceVerification.Tests;

/// <summary>BIO-IDN-001-QA-01-shaped governance checks, mirroring every prior BIO-BIO story's
/// own.</summary>
public sealed class FaceVerificationGovernanceTests
{
    [Fact]
    public void IFaceVerificationEvidenceStore_and_InMemory_implementation_are_not_public()
    {
        Assert.False(typeof(IFaceVerificationEvidenceStore).IsPublic);
        Assert.False(typeof(InMemoryFaceVerificationEvidenceStore).IsPublic);
    }

    [Fact]
    public void FaceVerificationService_is_not_public()
    {
        Assert.False(typeof(FaceVerificationService).IsPublic);
    }

    [Fact]
    public async Task GetAsync_cross_tenant_lookup_returns_null_not_an_exception()
    {
        var store = new InMemoryFaceVerificationEvidenceStore();
        var evidence = new FaceVerificationEvidence
        {
            Id = "ev1", TenantId = "ten_a", PersonId = "per_a", ReferenceId = "ref1", SourcePhotoMasterVersion = 1,
            ComparisonOutcome = FaceComparisonOutcome.Matched, Score = 1.0, ThresholdUsed = 0.8, MatcherId = "m",
            ProviderId = "p", ProviderVersion = "1.0", QualityFaceCount = 1, QualityApproved = true, OccurredAtUtc = "2026-01-01T00:00:00Z",
        };
        await store.SaveAsync(evidence, CancellationToken.None);

        var result = await store.GetAsync("ten_b", "ev1", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetForPersonAsync_never_returns_another_tenants_evidence()
    {
        var store = new InMemoryFaceVerificationEvidenceStore();
        var evidenceA = new FaceVerificationEvidence
        {
            Id = "ev-a", TenantId = "ten_a", PersonId = "per_shared", ReferenceId = "ref1", SourcePhotoMasterVersion = 1,
            ComparisonOutcome = FaceComparisonOutcome.NotMatched, ThresholdUsed = 0.8,
            ProviderId = "p", ProviderVersion = "1.0", QualityFaceCount = 1, QualityApproved = true, OccurredAtUtc = "2026-01-01T00:00:00Z",
        };
        var evidenceB = evidenceA with { Id = "ev-b", TenantId = "ten_b" };
        await store.SaveAsync(evidenceA, CancellationToken.None);
        await store.SaveAsync(evidenceB, CancellationToken.None);

        var history = await store.GetForPersonAsync("ten_a", "per_shared", CancellationToken.None);

        var only = Assert.Single(history);
        Assert.Equal("ev-a", only.Id);
    }
}

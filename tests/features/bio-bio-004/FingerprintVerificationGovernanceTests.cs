using Biolog.Features.FingerprintVerification.Domain;
using Biolog.Features.FingerprintVerification.Service;
using Xunit;

namespace Biolog.Features.FingerprintVerification.Tests;

/// <summary>BIO-IDN-001-QA-01-shaped governance checks, mirroring BIO-BIO-002/003's own.</summary>
public sealed class FingerprintVerificationGovernanceTests
{
    [Fact]
    public void IFingerprintVerificationRecordStore_and_InMemory_implementation_are_not_public()
    {
        Assert.False(typeof(IFingerprintVerificationRecordStore).IsPublic);
        Assert.False(typeof(InMemoryFingerprintVerificationRecordStore).IsPublic);
    }

    [Fact]
    public void FingerprintVerificationService_is_not_public()
    {
        Assert.False(typeof(FingerprintVerificationService).IsPublic);
    }

    [Fact]
    public async Task GetAsync_cross_tenant_lookup_returns_null_not_an_exception()
    {
        var store = new InMemoryFingerprintVerificationRecordStore();
        var record = new FingerprintVerificationRecord
        {
            Id = "rec1",
            TenantId = "ten_a",
            PersonId = "per_a",
            ComparisonOutcome = BiometricComparisonOutcome.Matched,
            AccessDecision = AccessDecision.Allow,
            Score = 1.0,
            MatcherId = "synthetic-exact-match-v1",
            MatchedTemplateId = "template-1",
            ProviderId = "synthetic-fingerprint-v1",
            ProviderVersion = "synthetic-fingerprint-1.0.0",
            OccurredAtUtc = "2026-01-01T00:00:00Z",
        };
        await store.SaveAsync(record, CancellationToken.None);

        var result = await store.GetAsync("ten_b", "rec1", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetForPersonAsync_never_returns_another_tenants_records()
    {
        var store = new InMemoryFingerprintVerificationRecordStore();
        var recordA = new FingerprintVerificationRecord
        {
            Id = "rec-a",
            TenantId = "ten_a",
            PersonId = "per_shared",
            ComparisonOutcome = BiometricComparisonOutcome.NotMatched,
            AccessDecision = AccessDecision.Deny,
            DenyReason = "no_match",
            ProviderId = "synthetic-fingerprint-v1",
            ProviderVersion = "synthetic-fingerprint-1.0.0",
            OccurredAtUtc = "2026-01-01T00:00:00Z",
        };
        var recordB = recordA with { Id = "rec-b", TenantId = "ten_b" };
        await store.SaveAsync(recordA, CancellationToken.None);
        await store.SaveAsync(recordB, CancellationToken.None);

        var history = await store.GetForPersonAsync("ten_a", "per_shared", CancellationToken.None);

        var only = Assert.Single(history);
        Assert.Equal("rec-a", only.Id);
    }
}

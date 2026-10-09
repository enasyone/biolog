using Biolog.Features.Biometrics.Domain;
using Biolog.Features.FaceSearch.Audit;
using Biolog.Features.FaceSearch.Domain;
using Biolog.Features.FaceSearch.Service;
using Xunit;

namespace Biolog.Features.FaceSearch.Tests;

/// <summary>BIO-IDN-001-QA-01-shaped governance checks, mirroring every prior BIO-BIO story's
/// own.</summary>
public sealed class FaceSearchGovernanceTests
{
    [Fact]
    public void IFaceGalleryStore_and_InMemory_implementation_are_not_public()
    {
        Assert.False(typeof(IFaceGalleryStore).IsPublic);
        Assert.False(typeof(InMemoryFaceGalleryStore).IsPublic);
    }

    [Fact]
    public void IFaceSearchAuditSink_and_InMemory_implementation_are_not_public()
    {
        Assert.False(typeof(IFaceSearchAuditSink).IsPublic);
        Assert.False(typeof(InMemoryFaceSearchAuditSink).IsPublic);
    }

    [Fact]
    public void FaceSearchService_is_not_public()
    {
        Assert.False(typeof(FaceSearchService).IsPublic);
    }

    [Fact]
    public async Task GalleryStore_cross_tenant_lookup_never_returns_another_tenants_templates()
    {
        var store = new InMemoryFaceGalleryStore();
        var template = BiometricTemplate.Create(
            "t1", "ten_a", "per_a", BiometricModality.Face, "prov", "1.0", [1], "2026-01-01T00:00:00Z");
        await store.IndexAsync(template, CancellationToken.None);

        var tenantB = await store.GetAllForTenantAsync("ten_b", CancellationToken.None);

        Assert.Empty(tenantB);
    }

    [Fact]
    public async Task AuditSink_ExistsAsync_resolves_an_ambiguous_write()
    {
        var sink = new InMemoryFaceSearchAuditSink();
        var auditEvent = new FaceSearchAuditEvent
        {
            EventId = "ev1", SearchId = "s1", TenantId = "ten_a", ActorId = "per_actor", SubjectPersonId = "per_subject",
            PopulationScope = "tenant_gallery", PopulationSize = 0, CandidateCount = 0, OccurredAtUtc = "2026-01-01T00:00:00Z",
        };
        await sink.WriteAsync(auditEvent, CancellationToken.None);

        var exists = await sink.ExistsAsync("ev1", CancellationToken.None);

        Assert.True(exists);
    }
}

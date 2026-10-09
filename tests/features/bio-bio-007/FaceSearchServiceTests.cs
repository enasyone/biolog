using Biolog.Features.Biometrics.Matching;
using Biolog.Features.Biometrics.Providers;
using Biolog.Features.Biometrics.Synthetic;
using Biolog.Features.FaceSearch.Audit;
using Biolog.Features.FaceSearch.Domain;
using Biolog.Features.FaceSearch.Service;
using Xunit;

namespace Biolog.Features.FaceSearch.Tests;

public sealed class FaceSearchServiceTests
{
    private const string NowUtc = "2026-01-01T00:00:00Z";
    private const string TenantA = "ten_a";
    private const string ActorId = "per_actor";

    private static (FaceSearchService Service, InMemoryFaceGalleryStore Gallery, SyntheticFaceSearchFeatureFlagPolicy FeatureFlag,
        FakeFaceGalleryApprovalGateway ApprovalGateway) NewService(IBiometricMatcher? matcher = null)
    {
        var gallery = new InMemoryFaceGalleryStore();
        var provider = new SyntheticFaceProvider();
        var featureFlag = new SyntheticFaceSearchFeatureFlagPolicy();
        var auditSink = new InMemoryFaceSearchAuditSink();
        var approvalGateway = new FakeFaceGalleryApprovalGateway();
        var counter = 1;
        var service = new FaceSearchService(
            gallery, approvalGateway, provider, matcher ?? new SyntheticBiometricMatcher(), featureFlag, auditSink,
            newId: () => $"id-{counter++}", nowUtc: () => NowUtc);
        return (service, gallery, featureFlag, approvalGateway);
    }

    private static BiometricSample Sample(byte[] payload) => new() { Payload = payload, ContentType = "application/octet-stream", CapturedAtUtc = NowUtc };

    /// <summary>Test helper: approves <paramref name="payload"/> as the person's current
    /// reference (simulating a BIO-BIO-003-approved capture) and then indexes it - mirrors the
    /// real two-step "capture is approved elsewhere, THEN indexed here" flow AC01 (BIO-BIO-007-
    /// QA-01) now enforces.</summary>
    private static async Task IndexAsync(
        FaceSearchService service, FakeFaceGalleryApprovalGateway approvalGateway, string tenantId, string personId, byte[] payload)
    {
        approvalGateway.Approve(tenantId, personId, payload);
        await service.IndexAsync(tenantId, personId, CancellationToken.None);
    }

    // --- AC01: search returns ranked candidates with similarity ---

    [Fact]
    public async Task AC01_ranked_candidates_are_ordered_by_score_descending()
    {
        var matcher = new FakeScoredBiometricMatcher(new Dictionary<string, double> { ["per_b"] = 0.3, ["per_c"] = 0.9, ["per_d"] = 0.6 });
        var (service, _, featureFlag, approvalGateway) = NewService(matcher);
        featureFlag.EnableForTenant(TenantA);
        await IndexAsync(service, approvalGateway, TenantA, "per_b", [1]);
        await IndexAsync(service, approvalGateway, TenantA, "per_c", [2]);
        await IndexAsync(service, approvalGateway, TenantA, "per_d", [3]);

        var result = await service.SearchAsync(TenantA, "per_subject", ActorId, Sample([9]), maxCandidates: 10, CancellationToken.None);

        Assert.Equal(["per_c", "per_d", "per_b"], result.RankedCandidates.Select(c => c.PersonId));
        Assert.Equal(0.9, result.RankedCandidates[0].Score);
    }

    [Fact]
    public async Task AC01_candidate_count_is_capped_at_maxCandidates()
    {
        var matcher = new FakeScoredBiometricMatcher(new Dictionary<string, double> { ["per_b"] = 0.1, ["per_c"] = 0.2, ["per_d"] = 0.3 });
        var (service, _, featureFlag, approvalGateway) = NewService(matcher);
        featureFlag.EnableForTenant(TenantA);
        await IndexAsync(service, approvalGateway, TenantA, "per_b", [1]);
        await IndexAsync(service, approvalGateway, TenantA, "per_c", [2]);
        await IndexAsync(service, approvalGateway, TenantA, "per_d", [3]);

        var result = await service.SearchAsync(TenantA, "per_subject", ActorId, Sample([9]), maxCandidates: 2, CancellationToken.None);

        Assert.Equal(2, result.RankedCandidates.Count);
    }

    [Fact]
    public async Task AC01_the_subject_is_never_matched_against_their_own_gallery_entry()
    {
        var (service, _, featureFlag, approvalGateway) = NewService();
        featureFlag.EnableForTenant(TenantA);
        await IndexAsync(service, approvalGateway, TenantA, "per_subject", [1, 2, 3]);
        await IndexAsync(service, approvalGateway, TenantA, "per_other", [9, 9, 9]);

        var result = await service.SearchAsync(TenantA, "per_subject", ActorId, Sample([1, 2, 3]), maxCandidates: 10, CancellationToken.None);

        Assert.DoesNotContain(result.RankedCandidates, c => c.PersonId == "per_subject");
    }

    [Fact]
    public async Task AC01_maxCandidates_must_be_positive()
    {
        var (service, _, featureFlag, approvalGateway) = NewService();
        featureFlag.EnableForTenant(TenantA);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => service.SearchAsync(TenantA, "per_subject", ActorId, Sample([1]), maxCandidates: 0, CancellationToken.None));
    }

    // --- BIO-BIO-007-QA-01 (Chioma acceptance, High): the gallery only accepts APPROVED references ---

    [Fact]
    public async Task QA01_indexing_throws_when_the_person_has_no_approved_reference()
    {
        var (service, _, featureFlag, _) = NewService();
        featureFlag.EnableForTenant(TenantA);

        var ex = await Assert.ThrowsAsync<FaceSearchException>(
            () => service.IndexAsync(TenantA, "per_unapproved", CancellationToken.None));

        Assert.Equal("face_search_no_approved_reference", ex.Code);
    }

    [Fact]
    public void QA01_IndexAsync_takes_no_raw_BiometricSample_parameter()
    {
        // The whole point of the fix: there is no overload of IndexAsync a caller could use to
        // push an unapproved raw capture straight into the gallery.
        var indexMethods = typeof(FaceSearchService).GetMethods().Where(m => m.Name == "IndexAsync");

        Assert.All(indexMethods, m => Assert.DoesNotContain(m.GetParameters(), p => p.ParameterType.Name == "BiometricSample"));
    }

    [Fact]
    public async Task QA01_only_an_approved_reference_can_enter_the_gallery_and_be_found_by_search()
    {
        var (service, _, featureFlag, approvalGateway) = NewService();
        featureFlag.EnableForTenant(TenantA);
        approvalGateway.Approve(TenantA, "per_approved", [1, 2, 3]);

        await service.IndexAsync(TenantA, "per_approved", CancellationToken.None);
        var result = await service.SearchAsync(TenantA, "per_subject", ActorId, Sample([1, 2, 3]), maxCandidates: 10, CancellationToken.None);

        var candidate = Assert.Single(result.RankedCandidates);
        Assert.Equal("per_approved", candidate.PersonId);
    }

    // --- AC02: 1:N requires explicit tenant feature enablement ---

    [Fact]
    public async Task AC02_search_is_rejected_for_a_tenant_that_never_explicitly_enabled_the_feature()
    {
        var (service, _, _, _) = NewService();

        var ex = await Assert.ThrowsAsync<FaceSearchException>(
            () => service.SearchAsync(TenantA, "per_subject", ActorId, Sample([1]), maxCandidates: 10, CancellationToken.None));

        Assert.Equal("face_search_feature_not_enabled", ex.Code);
    }

    [Fact]
    public async Task AC02_search_succeeds_once_the_tenant_explicitly_enables_the_feature()
    {
        var (service, _, featureFlag, approvalGateway) = NewService();
        featureFlag.EnableForTenant(TenantA);

        var result = await service.SearchAsync(TenantA, "per_subject", ActorId, Sample([1]), maxCandidates: 10, CancellationToken.None);

        Assert.NotNull(result);
    }

    [Fact]
    public async Task AC02_enabling_one_tenant_does_not_enable_another()
    {
        var (service, _, featureFlag, approvalGateway) = NewService();
        featureFlag.EnableForTenant("ten_other");

        var ex = await Assert.ThrowsAsync<FaceSearchException>(
            () => service.SearchAsync(TenantA, "per_subject", ActorId, Sample([1]), maxCandidates: 10, CancellationToken.None));

        Assert.Equal("face_search_feature_not_enabled", ex.Code);
    }

    [Fact]
    public async Task AC02_disabling_a_previously_enabled_tenant_blocks_further_searches()
    {
        var (service, _, featureFlag, approvalGateway) = NewService();
        featureFlag.EnableForTenant(TenantA);
        await service.SearchAsync(TenantA, "per_subject", ActorId, Sample([1]), maxCandidates: 10, CancellationToken.None);

        featureFlag.DisableForTenant(TenantA);

        var ex = await Assert.ThrowsAsync<FaceSearchException>(
            () => service.SearchAsync(TenantA, "per_subject", ActorId, Sample([1]), maxCandidates: 10, CancellationToken.None));
        Assert.Equal("face_search_feature_not_enabled", ex.Code);
    }

    // --- AC03: no automatic authorization is granted from search alone ---

    [Fact]
    public void AC03_the_result_and_candidate_types_carry_no_authorization_or_decision_field()
    {
        var resultProperties = typeof(FaceSearchResult).GetProperties().Select(p => p.Name);
        var candidateProperties = typeof(FaceSearchCandidate).GetProperties().Select(p => p.Name);

        Assert.DoesNotContain(resultProperties, n => n.Contains("Decision", StringComparison.OrdinalIgnoreCase) || n.Contains("Authoriz", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(candidateProperties, n => n.Contains("Decision", StringComparison.OrdinalIgnoreCase) || n.Contains("Authoriz", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AC03_the_service_exposes_no_decide_or_authorize_method()
    {
        var methodNames = typeof(FaceSearchService).GetMethods().Select(m => m.Name);

        Assert.DoesNotContain(methodNames, n => n.Contains("Decide", StringComparison.OrdinalIgnoreCase) || n.Contains("Authoriz", StringComparison.OrdinalIgnoreCase));
    }

    // --- AC04: no automatic PERSON merge occurs ---

    [Fact]
    public void AC04_this_assembly_has_no_reference_to_BIO_IDN_001s_own_assembly()
    {
        var referenced = typeof(FaceSearchService).Assembly.GetReferencedAssemblies().Select(a => a.Name).ToList();
        Assert.DoesNotContain("Biolog.Features.PersonRegistry", referenced);
    }

    // --- AC05: population scope and audit are explicit ---

    [Fact]
    public async Task AC05_the_result_names_the_population_scope_and_size_explicitly()
    {
        var (service, _, featureFlag, approvalGateway) = NewService();
        featureFlag.EnableForTenant(TenantA);
        await IndexAsync(service, approvalGateway, TenantA, "per_a", [1]);
        await IndexAsync(service, approvalGateway, TenantA, "per_b", [2]);

        var result = await service.SearchAsync(TenantA, "per_subject", ActorId, Sample([9]), maxCandidates: 10, CancellationToken.None);

        Assert.Equal(FaceSearchService.PopulationScope, result.PopulationScope);
        Assert.Equal(2, result.PopulationSize);
    }

    [Fact]
    public async Task AC05_every_search_writes_an_audit_event()
    {
        var (service, _, featureFlag, approvalGateway) = NewService();
        featureFlag.EnableForTenant(TenantA);
        await IndexAsync(service, approvalGateway, TenantA, "per_a", [1]);

        var result = await service.SearchAsync(TenantA, "per_subject", ActorId, Sample([9]), maxCandidates: 10, CancellationToken.None);

        var trail = await service.ListAuditTrailAsync(TenantA, CancellationToken.None);
        var entry = Assert.Single(trail);
        Assert.Equal(result.SearchId, entry.SearchId);
        Assert.Equal(ActorId, entry.ActorId);
        Assert.Equal("per_subject", entry.SubjectPersonId);
        Assert.Equal(FaceSearchService.PopulationScope, entry.PopulationScope);
        Assert.Equal(1, entry.PopulationSize);
        Assert.Equal(result.RankedCandidates.Count, entry.CandidateCount);
    }

    [Fact]
    public async Task AC05_a_gallery_entry_indexed_for_one_tenant_is_invisible_to_another_tenants_search()
    {
        var (service, _, featureFlag, approvalGateway) = NewService();
        featureFlag.EnableForTenant("ten_a");
        featureFlag.EnableForTenant("ten_b");
        await IndexAsync(service, approvalGateway, "ten_a", "per_a", [1, 2, 3]);

        var result = await service.SearchAsync("ten_b", "per_subject", ActorId, Sample([1, 2, 3]), maxCandidates: 10, CancellationToken.None);

        Assert.Empty(result.RankedCandidates);
        Assert.Equal(0, result.PopulationSize);
    }

    [Fact]
    public async Task AC05_audit_trail_never_leaks_another_tenants_events()
    {
        var (service, _, featureFlag, approvalGateway) = NewService();
        featureFlag.EnableForTenant("ten_a");
        await service.SearchAsync("ten_a", "per_subject", ActorId, Sample([1]), maxCandidates: 10, CancellationToken.None);

        var tenantBTrail = await service.ListAuditTrailAsync("ten_b", CancellationToken.None);

        Assert.Empty(tenantBTrail);
    }

    /// <summary>BIO-BIO-009-AC02/AC04: approved, additive extension (Kemi, 2026-10-06) - a plain
    /// persistence wrapper, matching <see cref="FaceSearchService.IndexAsync"/>'s own directness.
    /// No lock, no audit - this service's gallery operations have neither today.</summary>
    [Fact]
    public async Task RemoveFromGalleryAsync_removes_an_indexed_identity()
    {
        var (service, gallery, _, approvalGateway) = NewService();
        await IndexAsync(service, approvalGateway, TenantA, "per_b", [1]);

        var removed = await service.RemoveFromGalleryAsync(TenantA, "per_b", CancellationToken.None);

        Assert.True(removed);
        Assert.Empty(await gallery.GetAllForTenantAsync(TenantA, CancellationToken.None));
    }

    [Fact]
    public async Task RemoveFromGalleryAsync_returns_false_when_nothing_is_indexed_for_that_identity()
    {
        var (service, _, _, _) = NewService();

        var removed = await service.RemoveFromGalleryAsync(TenantA, "per_never_indexed", CancellationToken.None);

        Assert.False(removed);
    }
}

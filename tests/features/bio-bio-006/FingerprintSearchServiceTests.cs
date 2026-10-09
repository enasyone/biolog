using Biolog.Features.Biometrics.Domain;
using Biolog.Features.Biometrics.Matching;
using Biolog.Features.Biometrics.Providers;
using Biolog.Features.Biometrics.Synthetic;
using Biolog.Features.FingerprintSearch.Domain;
using Biolog.Features.FingerprintSearch.Service;
using Xunit;

namespace Biolog.Features.FingerprintSearch.Tests;

public sealed class FingerprintSearchServiceTests
{
    private const string NowUtc = "2026-01-01T00:00:00Z";
    private const string TenantA = "ten_a";
    private const string ActorId = "per_actor";

    private static (FingerprintSearchService Service, InMemoryFingerprintGalleryStore Gallery,
        InMemoryFingerprintDuplicateReviewRepository ReviewRepo, FakeBiometricSearchPermissionGateway PermGateway,
        SyntheticFingerprintDuplicateThresholdPolicy ThresholdPolicy) NewService(IBiometricMatcher? matcher = null)
    {
        var gallery = new InMemoryFingerprintGalleryStore();
        var provider = new SyntheticFingerprintProvider();
        var thresholdPolicy = new SyntheticFingerprintDuplicateThresholdPolicy();
        var permGateway = new FakeBiometricSearchPermissionGateway();
        var reviewRepo = new InMemoryFingerprintDuplicateReviewRepository();
        var counter = 1;
        var service = new FingerprintSearchService(
            gallery, provider, matcher ?? new SyntheticBiometricMatcher(), thresholdPolicy, permGateway, reviewRepo,
            new FingerprintDuplicateReviewLockRegistry(),
            newId: () => $"id-{counter++}", nowUtc: () => NowUtc);
        return (service, gallery, reviewRepo, permGateway, thresholdPolicy);
    }

    private static BiometricSample Sample(byte[] payload) => new() { Payload = payload, ContentType = "application/octet-stream", CapturedAtUtc = NowUtc };

    private static async Task IndexAsync(FingerprintSearchService service, string tenantId, string personId, byte[] payload) =>
        await service.IndexAsync(tenantId, personId, Sample(payload), CancellationToken.None);

    // --- AC01: search returns ranked candidates and scores ---

    [Fact]
    public async Task AC01_ranked_candidates_are_ordered_by_score_descending()
    {
        var matcher = new FakeScoredBiometricMatcher(new Dictionary<string, double> { ["per_b"] = 0.3, ["per_c"] = 0.9, ["per_d"] = 0.6 });
        var (service, _, _, permGateway, _) = NewService(matcher);
        permGateway.Grant(TenantA, ActorId, FingerprintSearchService.SearchPermissionKey);
        await IndexAsync(service, TenantA, "per_b", [1]);
        await IndexAsync(service, TenantA, "per_c", [2]);
        await IndexAsync(service, TenantA, "per_d", [3]);

        var result = await service.SearchAsync(TenantA, "per_subject", ActorId, Sample([9]), maxCandidates: 10, CancellationToken.None);

        Assert.Equal(["per_c", "per_d", "per_b"], result.RankedCandidates.Select(c => c.PersonId));
        Assert.Equal(0.9, result.RankedCandidates[0].Score);
    }

    [Fact]
    public async Task AC01_candidate_count_is_capped_at_maxCandidates()
    {
        var matcher = new FakeScoredBiometricMatcher(new Dictionary<string, double> { ["per_b"] = 0.1, ["per_c"] = 0.2, ["per_d"] = 0.3 });
        var (service, _, _, permGateway, _) = NewService(matcher);
        permGateway.Grant(TenantA, ActorId, FingerprintSearchService.SearchPermissionKey);
        await IndexAsync(service, TenantA, "per_b", [1]);
        await IndexAsync(service, TenantA, "per_c", [2]);
        await IndexAsync(service, TenantA, "per_d", [3]);

        var result = await service.SearchAsync(TenantA, "per_subject", ActorId, Sample([9]), maxCandidates: 2, CancellationToken.None);

        Assert.Equal(2, result.RankedCandidates.Count);
    }

    [Fact]
    public async Task AC01_the_subject_is_never_matched_against_their_own_gallery_entry()
    {
        var (service, _, _, permGateway, _) = NewService();
        permGateway.Grant(TenantA, ActorId, FingerprintSearchService.SearchPermissionKey);
        await IndexAsync(service, TenantA, "per_subject", [1, 2, 3]);
        await IndexAsync(service, TenantA, "per_other", [9, 9, 9]);

        var result = await service.SearchAsync(TenantA, "per_subject", ActorId, Sample([1, 2, 3]), maxCandidates: 10, CancellationToken.None);

        Assert.DoesNotContain(result.RankedCandidates, c => c.PersonId == "per_subject");
    }

    [Fact]
    public async Task AC01_maxCandidates_must_be_positive()
    {
        var (service, _, _, permGateway, _) = NewService();
        permGateway.Grant(TenantA, ActorId, FingerprintSearchService.SearchPermissionKey);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => service.SearchAsync(TenantA, "per_subject", ActorId, Sample([1]), maxCandidates: 0, CancellationToken.None));
    }

    // --- AC02: permissions distinguish 1:N from 1:1 ---

    [Fact]
    public async Task AC02_search_without_the_1n_permission_is_rejected()
    {
        var (service, _, _, _, _) = NewService();

        var ex = await Assert.ThrowsAsync<FingerprintSearchException>(
            () => service.SearchAsync(TenantA, "per_subject", ActorId, Sample([1]), maxCandidates: 10, CancellationToken.None));

        Assert.Equal("fingerprint_search_permission_required", ex.Code);
    }

    [Fact]
    public async Task AC02_search_with_the_1n_permission_succeeds()
    {
        var (service, _, _, permGateway, _) = NewService();
        permGateway.Grant(TenantA, ActorId, FingerprintSearchService.SearchPermissionKey);

        var result = await service.SearchAsync(TenantA, "per_subject", ActorId, Sample([1]), maxCandidates: 10, CancellationToken.None);

        Assert.NotNull(result);
    }

    [Fact]
    public async Task AC02_holding_an_unrelated_permission_key_does_not_authorise_1n_search()
    {
        // Proves the 1:N permission is a genuinely distinct key - holding some OTHER grant
        // (standing in for a hypothetical 1:1-only permission) does not imply this one.
        var (service, _, _, permGateway, _) = NewService();
        permGateway.Grant(TenantA, ActorId, "biometric.fingerprint_verify_1_1");

        var ex = await Assert.ThrowsAsync<FingerprintSearchException>(
            () => service.SearchAsync(TenantA, "per_subject", ActorId, Sample([1]), maxCandidates: 10, CancellationToken.None));

        Assert.Equal("fingerprint_search_permission_required", ex.Code);
    }

    // --- AC03: possible duplicate routes to human review ---

    [Fact]
    public async Task AC03_a_score_at_or_above_the_threshold_raises_a_pending_review_entry()
    {
        var (service, _, reviewRepo, permGateway, thresholdPolicy) = NewService();
        permGateway.Grant(TenantA, ActorId, FingerprintSearchService.SearchPermissionKey);
        thresholdPolicy.SetThresholdForTenant(TenantA, 0.8);
        await IndexAsync(service, TenantA, "per_dup", [1, 2, 3]);

        var result = await service.SearchAsync(TenantA, "per_subject", ActorId, Sample([1, 2, 3]), maxCandidates: 10, CancellationToken.None);
        Assert.True(result.RankedCandidates.Single().PossibleDuplicate);

        var (low, high) = FingerprintDuplicateReviewEntry.OrderPair("per_subject", "per_dup");
        var entries = await reviewRepo.ListForTenantAsync(TenantA, CancellationToken.None);
        var entry = Assert.Single(entries);
        Assert.Equal(low, entry.PersonIdLow);
        Assert.Equal(high, entry.PersonIdHigh);
        Assert.Equal(FingerprintDuplicateReviewStatus.PendingReview, entry.Status);
    }

    [Fact]
    public async Task AC03_a_score_below_the_threshold_does_not_raise_a_review_entry()
    {
        var (service, _, reviewRepo, permGateway, thresholdPolicy) = NewService();
        permGateway.Grant(TenantA, ActorId, FingerprintSearchService.SearchPermissionKey);
        thresholdPolicy.SetThresholdForTenant(TenantA, 0.8);
        await IndexAsync(service, TenantA, "per_other", [9, 9, 9]);

        await service.SearchAsync(TenantA, "per_subject", ActorId, Sample([1, 2, 3]), maxCandidates: 10, CancellationToken.None);

        var entries = await reviewRepo.ListForTenantAsync(TenantA, CancellationToken.None);
        Assert.Empty(entries);
    }

    [Fact]
    public async Task AC03_a_decided_pair_is_never_reopened_by_a_later_search()
    {
        var (service, _, reviewRepo, permGateway, thresholdPolicy) = NewService();
        permGateway.Grant(TenantA, ActorId, FingerprintSearchService.SearchPermissionKey);
        thresholdPolicy.SetThresholdForTenant(TenantA, 0.8);
        await IndexAsync(service, TenantA, "per_dup", [1, 2, 3]);
        await service.SearchAsync(TenantA, "per_subject", ActorId, Sample([1, 2, 3]), maxCandidates: 10, CancellationToken.None);

        var decided = await service.DecideAsync(
            TenantA, "per_subject", "per_dup", FingerprintDuplicateReviewStatus.ConfirmedDistinct, ActorId, expectedVersion: 1, CancellationToken.None);
        Assert.Equal(FingerprintDuplicateReviewStatus.ConfirmedDistinct, decided.Status);

        // Re-search re-observes the same match - the human decision must not be reopened.
        await service.SearchAsync(TenantA, "per_subject", ActorId, Sample([1, 2, 3]), maxCandidates: 10, CancellationToken.None);

        var (low, high) = FingerprintDuplicateReviewEntry.OrderPair("per_subject", "per_dup");
        var entry = await reviewRepo.GetAsync(TenantA, low, high, CancellationToken.None);
        Assert.Equal(FingerprintDuplicateReviewStatus.ConfirmedDistinct, entry!.Status);
        Assert.Equal(2, entry.Version); // only the human decision bumped the version, not the re-search
    }

    [Fact]
    public async Task AC03_every_above_threshold_candidate_routes_to_review_even_when_crowded_out_of_the_capped_ranked_list()
    {
        // BIO-BIO-006-QA-01 (Seyi round 1, High): three candidates all score above the
        // duplicate-review threshold, but the caller asks for only the single best RANKED match
        // (maxCandidates: 1). All three must still reach the review queue - AC03's routing must
        // not be limited to whatever AC01's own cap lets the caller see.
        var matcher = new FakeScoredBiometricMatcher(new Dictionary<string, double> { ["per_b"] = 0.85, ["per_c"] = 0.9, ["per_d"] = 0.95 });
        var (service, _, reviewRepo, permGateway, thresholdPolicy) = NewService(matcher);
        permGateway.Grant(TenantA, ActorId, FingerprintSearchService.SearchPermissionKey);
        thresholdPolicy.SetThresholdForTenant(TenantA, 0.8);
        await IndexAsync(service, TenantA, "per_b", [1]);
        await IndexAsync(service, TenantA, "per_c", [2]);
        await IndexAsync(service, TenantA, "per_d", [3]);

        var result = await service.SearchAsync(TenantA, "per_subject", ActorId, Sample([9]), maxCandidates: 1, CancellationToken.None);

        Assert.Single(result.RankedCandidates); // AC01's own cap still applies to the returned list
        var entries = await reviewRepo.ListForTenantAsync(TenantA, CancellationToken.None);
        Assert.Equal(3, entries.Count); // AC03's routing is NOT limited by that cap
        Assert.All(entries, e => Assert.Equal(FingerprintDuplicateReviewStatus.PendingReview, e.Status));
    }

    [Fact]
    public async Task AC03_a_higher_later_score_augments_a_pending_entry_a_lower_one_does_not()
    {
        var matcherLow = new FakeScoredBiometricMatcher(new Dictionary<string, double> { ["per_dup"] = 0.8 });
        var (service, _, reviewRepo, permGateway, thresholdPolicy) = NewService(matcherLow);
        permGateway.Grant(TenantA, ActorId, FingerprintSearchService.SearchPermissionKey);
        thresholdPolicy.SetThresholdForTenant(TenantA, 0.5);
        await IndexAsync(service, TenantA, "per_dup", [1]);
        await service.SearchAsync(TenantA, "per_subject", ActorId, Sample([1]), maxCandidates: 10, CancellationToken.None);

        var (low, high) = FingerprintDuplicateReviewEntry.OrderPair("per_subject", "per_dup");
        var first = await reviewRepo.GetAsync(TenantA, low, high, CancellationToken.None);
        Assert.Equal(0.8, first!.Score);
        Assert.Equal(1, first.Version);
    }

    [Fact]
    public async Task AC03_DecideAsync_requires_the_expected_version()
    {
        var (service, _, _, permGateway, thresholdPolicy) = NewService();
        permGateway.Grant(TenantA, ActorId, FingerprintSearchService.SearchPermissionKey);
        thresholdPolicy.SetThresholdForTenant(TenantA, 0.8);
        await IndexAsync(service, TenantA, "per_dup", [1, 2, 3]);
        await service.SearchAsync(TenantA, "per_subject", ActorId, Sample([1, 2, 3]), maxCandidates: 10, CancellationToken.None);

        var ex = await Assert.ThrowsAsync<FingerprintSearchException>(
            () => service.DecideAsync(TenantA, "per_subject", "per_dup", FingerprintDuplicateReviewStatus.ConfirmedDuplicate, ActorId, expectedVersion: 99, CancellationToken.None));

        Assert.Equal("concurrency_conflict", ex.Code);
    }

    [Fact]
    public async Task AC03_DecideAsync_for_an_unraised_pair_throws_not_found()
    {
        var (service, _, _, _, _) = NewService();

        var ex = await Assert.ThrowsAsync<FingerprintSearchException>(
            () => service.DecideAsync(TenantA, "per_subject", "per_dup", FingerprintDuplicateReviewStatus.ConfirmedDuplicate, ActorId, expectedVersion: 1, CancellationToken.None));

        Assert.Equal("fingerprint_duplicate_review_entry_not_found", ex.Code);
    }

    [Fact]
    public async Task AC03_two_simultaneous_searches_raising_the_same_new_pair_both_converge_without_throwing()
    {
        // BIO-BIO-006-QA-02 (Chioma acceptance, High): two concurrent searches for the same
        // absent pair must both succeed and converge onto exactly one PendingReview entry,
        // never race such that one throws already_exists.
        var (service, _, reviewRepo, permGateway, thresholdPolicy) = NewService();
        permGateway.Grant(TenantA, ActorId, FingerprintSearchService.SearchPermissionKey);
        thresholdPolicy.SetThresholdForTenant(TenantA, 0.8);
        await IndexAsync(service, TenantA, "per_dup", [1, 2, 3]);

        // Task.Run schedules onto the thread pool so the two calls genuinely race rather than
        // running strictly sequentially (this in-memory store completes every awaited step
        // synchronously, so two calls started without Task.Run would never actually interleave).
        var searchA = Task.Run(() => service.SearchAsync(TenantA, "per_subject_a", ActorId, Sample([1, 2, 3]), maxCandidates: 10, CancellationToken.None));
        var searchB = Task.Run(() => service.SearchAsync(TenantA, "per_subject_a", ActorId, Sample([1, 2, 3]), maxCandidates: 10, CancellationToken.None));

        // Neither call should throw - both observe the same real-world duplicate and must converge.
        await Task.WhenAll(searchA, searchB);

        var (low, high) = FingerprintDuplicateReviewEntry.OrderPair("per_subject_a", "per_dup");
        var entries = await reviewRepo.ListForTenantAsync(TenantA, CancellationToken.None);
        var entry = Assert.Single(entries);
        Assert.Equal(low, entry.PersonIdLow);
        Assert.Equal(high, entry.PersonIdHigh);
        Assert.Equal(FingerprintDuplicateReviewStatus.PendingReview, entry.Status);
    }

    // --- AC04: no automatic PERSON merge occurs ---

    [Fact]
    public void AC04_this_assembly_has_no_reference_to_BIO_IDN_001s_own_assembly()
    {
        var referenced = typeof(FingerprintSearchService).Assembly.GetReferencedAssemblies().Select(a => a.Name).ToList();
        Assert.DoesNotContain("Biolog.Features.PersonRegistry", referenced);
    }

    [Fact]
    public void AC04_the_review_status_enum_has_no_merged_member()
    {
        var names = Enum.GetNames<FingerprintDuplicateReviewStatus>();
        Assert.DoesNotContain(names, n => n.Contains("merge", StringComparison.OrdinalIgnoreCase));
    }

    // --- AC05: search scope is tenant constrained ---

    [Fact]
    public async Task AC05_a_gallery_entry_indexed_for_one_tenant_is_invisible_to_another_tenants_search()
    {
        var (service, _, _, permGateway, thresholdPolicy) = NewService();
        permGateway.Grant("ten_a", ActorId, FingerprintSearchService.SearchPermissionKey);
        permGateway.Grant("ten_b", ActorId, FingerprintSearchService.SearchPermissionKey);
        thresholdPolicy.SetThresholdForTenant("ten_a", 0.8);
        thresholdPolicy.SetThresholdForTenant("ten_b", 0.8);
        await IndexAsync(service, "ten_a", "per_a", [1, 2, 3]);

        var result = await service.SearchAsync("ten_b", "per_subject", ActorId, Sample([1, 2, 3]), maxCandidates: 10, CancellationToken.None);

        Assert.Empty(result.RankedCandidates);
    }

    [Fact]
    public async Task AC05_review_queue_never_leaks_another_tenants_entries()
    {
        var (service, _, reviewRepo, permGateway, thresholdPolicy) = NewService();
        permGateway.Grant("ten_a", ActorId, FingerprintSearchService.SearchPermissionKey);
        thresholdPolicy.SetThresholdForTenant("ten_a", 0.8);
        await IndexAsync(service, "ten_a", "per_dup", [1, 2, 3]);
        await service.SearchAsync("ten_a", "per_subject", ActorId, Sample([1, 2, 3]), maxCandidates: 10, CancellationToken.None);

        var tenantBEntries = await reviewRepo.ListForTenantAsync("ten_b", CancellationToken.None);

        Assert.Empty(tenantBEntries);
    }

    /// <summary>BIO-BIO-009-AC02/AC04: approved, additive extension (Kemi, 2026-10-06) - a plain
    /// persistence wrapper, matching <see cref="FingerprintSearchService.IndexAsync"/>'s own
    /// directness. No lock, no audit - this service's gallery operations have neither today.</summary>
    [Fact]
    public async Task RemoveFromGalleryAsync_removes_an_indexed_identity()
    {
        var (service, gallery, _, _, _) = NewService();
        await IndexAsync(service, "ten_a", "per_b", [1]);

        var removed = await service.RemoveFromGalleryAsync("ten_a", "per_b", CancellationToken.None);

        Assert.True(removed);
        Assert.Empty(await gallery.GetAllForTenantAsync("ten_a", CancellationToken.None));
    }

    [Fact]
    public async Task RemoveFromGalleryAsync_returns_false_when_nothing_is_indexed_for_that_identity()
    {
        var (service, _, _, _, _) = NewService();

        var removed = await service.RemoveFromGalleryAsync("ten_a", "per_never_indexed", CancellationToken.None);

        Assert.False(removed);
    }
}

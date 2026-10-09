using Biolog.Features.Biometrics.Matching;
using Biolog.Features.Biometrics.Providers;
using Biolog.Features.Biometrics.Synthetic;
using Biolog.Features.FaceEnrolment.Quality;
using Biolog.Features.FaceVerification.Domain;
using Biolog.Features.FaceVerification.Service;
using Xunit;

namespace Biolog.Features.FaceVerification.Tests;

public sealed class FaceVerificationServiceTests
{
    private const string NowUtc = "2026-01-01T00:00:00Z";

    private static (FaceVerificationService Service, FakeFaceReferenceGateway Gateway, SyntheticFaceVerificationThresholdPolicy ThresholdPolicy)
        NewService(IFaceProvider? provider = null, IBiometricMatcher? matcher = null)
    {
        var gateway = new FakeFaceReferenceGateway();
        var thresholdPolicy = new SyntheticFaceVerificationThresholdPolicy();
        var counter = 1;
        var service = new FaceVerificationService(
            gateway, provider ?? new SyntheticFaceProvider(), matcher ?? new SyntheticBiometricMatcher(), new SyntheticFaceQualityChecker(),
            thresholdPolicy, new InMemoryFaceVerificationEvidenceStore(),
            newId: () => $"id-{counter++}", nowUtc: () => NowUtc);
        return (service, gateway, thresholdPolicy);
    }

    private static BiometricSample Sample(byte[] payload) => new() { Payload = payload, ContentType = "application/octet-stream", CapturedAtUtc = NowUtc };

    // --- AC01: similarity, threshold policy, quality and model version are recorded ---

    [Fact]
    public async Task AC01_a_matched_verification_records_similarity_threshold_quality_and_model_version()
    {
        var (service, gateway, _) = NewService();
        gateway.Add("ten_a", "per_a", [1, 0, 9, 9, 9]);

        var evidence = await service.CompareAsync("ten_a", "per_a", Sample([1, 0, 9, 9, 9]), CancellationToken.None);

        Assert.Equal(1.0, evidence.Score);
        Assert.Equal(SyntheticFaceVerificationThresholdPolicy.DefaultThreshold, evidence.ThresholdUsed);
        Assert.Equal(1, evidence.QualityFaceCount);
        Assert.True(evidence.QualityApproved);
        Assert.Equal("synthetic-face-v1", evidence.ProviderId);
        Assert.Equal("synthetic-face-1.0.0", evidence.ProviderVersion);
        Assert.Equal(FaceComparisonOutcome.Matched, evidence.ComparisonOutcome);
    }

    [Fact]
    public async Task AC01_quality_is_recorded_even_when_the_live_capture_has_poor_quality()
    {
        var (service, gateway, _) = NewService();
        gateway.Add("ten_a", "per_a", [1, 0, 9, 9, 9]);

        // Live capture: zero faces detected (payload[0] = 0).
        var evidence = await service.CompareAsync("ten_a", "per_a", Sample([0, 0]), CancellationToken.None);

        Assert.Equal(0, evidence.QualityFaceCount);
        Assert.False(evidence.QualityApproved);
    }

    [Fact]
    public async Task AC01_every_verification_attempt_is_persisted_and_retrievable_by_id()
    {
        var (service, gateway, _) = NewService();
        gateway.Add("ten_a", "per_a", [1, 0, 9, 9, 9]);

        var evidence = await service.CompareAsync("ten_a", "per_a", Sample([1, 0, 9, 9, 9]), CancellationToken.None);
        var fetched = await service.GetEvidenceAsync("ten_a", evidence.Id, CancellationToken.None);

        Assert.NotNull(fetched);
        Assert.Equal(evidence.Id, fetched!.Id);
    }

    [Fact]
    public async Task AC01_history_lists_every_attempt_for_the_claimed_identity()
    {
        var (service, gateway, _) = NewService();
        gateway.Add("ten_a", "per_a", [1, 0, 9, 9, 9]);

        await service.CompareAsync("ten_a", "per_a", Sample([1, 0, 9, 9, 9]), CancellationToken.None);
        await service.CompareAsync("ten_a", "per_a", Sample([1, 0, 1, 1, 1]), CancellationToken.None);

        var history = await service.GetHistoryAsync("ten_a", "per_a", CancellationToken.None);

        Assert.Equal(2, history.Count);
    }

    // --- AC02: threshold is policy-based rather than arbitrary UI input ---

    [Fact]
    public async Task AC02_the_same_score_is_accepted_under_a_low_threshold_and_rejected_under_a_high_one()
    {
        var (lowService, lowGateway, lowPolicy) = NewService(matcher: new FakeBiometricMatcher(0.5));
        lowGateway.Add("ten_a", "per_a", [1, 0, 9, 9, 9]);
        lowPolicy.SetThresholdForTenant("ten_a", 0.3);

        var lowThresholdEvidence = await lowService.CompareAsync("ten_a", "per_a", Sample([1, 0, 9, 9, 9]), CancellationToken.None);
        Assert.Equal(FaceComparisonOutcome.Matched, lowThresholdEvidence.ComparisonOutcome);
        Assert.Equal(0.3, lowThresholdEvidence.ThresholdUsed);

        var (highService, highGateway, highPolicy) = NewService(matcher: new FakeBiometricMatcher(0.5));
        highGateway.Add("ten_a", "per_a", [1, 0, 9, 9, 9]);
        highPolicy.SetThresholdForTenant("ten_a", 0.9);

        var highThresholdEvidence = await highService.CompareAsync("ten_a", "per_a", Sample([1, 0, 9, 9, 9]), CancellationToken.None);
        Assert.Equal(FaceComparisonOutcome.NotMatched, highThresholdEvidence.ComparisonOutcome);
        Assert.Equal(0.9, highThresholdEvidence.ThresholdUsed);
    }

    [Fact]
    public async Task AC02_the_threshold_used_is_always_the_policy_resolved_value_never_a_caller_supplied_one()
    {
        // CompareAsync's own signature has no threshold parameter at all (see the structural
        // suite for the compile-time proof) - this proves the resolved value always lands on the
        // evidence unmodified.
        var (service, gateway, policy) = NewService();
        gateway.Add("ten_a", "per_a", [1, 0, 9, 9, 9]);
        policy.SetThresholdForTenant("ten_a", 0.42);

        var evidence = await service.CompareAsync("ten_a", "per_a", Sample([1, 0, 9, 9, 9]), CancellationToken.None);

        Assert.Equal(0.42, evidence.ThresholdUsed);
    }

    [Fact]
    public void AC02_the_default_synthetic_policy_rejects_an_out_of_range_threshold()
    {
        var policy = new SyntheticFaceVerificationThresholdPolicy();
        Assert.Throws<ArgumentOutOfRangeException>(() => policy.SetThresholdForTenant("ten_a", 1.5));
        Assert.Throws<ArgumentOutOfRangeException>(() => policy.SetThresholdForTenant("ten_a", -0.1));
    }

    // --- AC03: verification evidence is separate from authorization decision ---

    [Fact]
    public async Task AC03_a_matched_comparison_produces_evidence_with_no_authorization_field_and_decides_allow_separately()
    {
        var (service, gateway, _) = NewService();
        gateway.Add("ten_a", "per_a", [1, 0, 9, 9, 9]);

        var evidence = await service.CompareAsync("ten_a", "per_a", Sample([1, 0, 9, 9, 9]), CancellationToken.None);
        var decision = FaceAuthorizationPolicy.Decide(evidence);

        Assert.Equal(FaceComparisonOutcome.Matched, evidence.ComparisonOutcome);
        Assert.Equal(AccessDecision.Allow, decision.Decision);
        Assert.Equal(evidence.Id, decision.EvidenceId);
        Assert.Null(decision.DenyReason);
    }

    [Fact]
    public void AC03_deciding_the_same_evidence_twice_is_a_pure_repeatable_operation()
    {
        var evidence = new FaceVerificationEvidence
        {
            Id = "ev1", TenantId = "ten_a", PersonId = "per_a", ReferenceId = "ref1", SourcePhotoMasterVersion = 1,
            ComparisonOutcome = FaceComparisonOutcome.Matched, Score = 1.0, ThresholdUsed = 0.8, MatcherId = "m",
            ProviderId = "p", ProviderVersion = "1.0", QualityFaceCount = 1, QualityApproved = true, OccurredAtUtc = NowUtc,
        };

        var first = FaceAuthorizationPolicy.Decide(evidence);
        var second = FaceAuthorizationPolicy.Decide(evidence);

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task AC03_a_not_matched_comparison_decides_deny_with_no_match_reason()
    {
        var (service, gateway, _) = NewService();
        gateway.Add("ten_a", "per_a", [1, 0, 9, 9, 9]);

        var evidence = await service.CompareAsync("ten_a", "per_a", Sample([1, 0, 1, 1, 1]), CancellationToken.None);
        var decision = FaceAuthorizationPolicy.Decide(evidence);

        Assert.Equal(FaceComparisonOutcome.NotMatched, evidence.ComparisonOutcome);
        Assert.Equal(AccessDecision.Deny, decision.Decision);
        Assert.Equal("no_match", decision.DenyReason);
    }

    // --- AC04: reference/photo lifecycle changes are handled deterministically ---

    [Fact]
    public async Task AC04_no_approved_reference_throws_a_precondition_error_not_a_routine_outcome()
    {
        var (service, _, _) = NewService();

        var ex = await Assert.ThrowsAsync<FaceVerificationException>(
            () => service.CompareAsync("ten_a", "per_a", Sample([1, 0, 9, 9, 9]), CancellationToken.None));

        Assert.Equal("face_verification_no_authorised_reference", ex.Code);
    }

    [Fact]
    public async Task AC04_a_superseded_reference_is_never_used_the_latest_one_always_is()
    {
        var (service, gateway, _) = NewService();
        gateway.Add("ten_a", "per_a", [1, 0, 8, 8, 8]); // first capture
        gateway.Add("ten_a", "per_a", [1, 0, 9, 9, 9]); // re-capture supersedes it

        var evidence = await service.CompareAsync("ten_a", "per_a", Sample([1, 0, 9, 9, 9]), CancellationToken.None);

        // Only matches the NEWEST reference's bytes, proving the superseded one was not used.
        Assert.Equal(FaceComparisonOutcome.Matched, evidence.ComparisonOutcome);
    }

    [Fact]
    public async Task AC04_cross_tenant_claimed_identity_has_no_authorised_reference()
    {
        var (service, gateway, _) = NewService();
        gateway.Add("ten_a", "per_a", [1, 0, 9, 9, 9]);

        var ex = await Assert.ThrowsAsync<FaceVerificationException>(
            () => service.CompareAsync("ten_b", "per_a", Sample([1, 0, 9, 9, 9]), CancellationToken.None));

        Assert.Equal("face_verification_no_authorised_reference", ex.Code);
    }
}

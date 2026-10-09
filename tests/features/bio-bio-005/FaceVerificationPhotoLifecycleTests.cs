using Biolog.Features.Biometrics.Providers;
using Biolog.Features.Biometrics.Synthetic;
using Biolog.Features.FaceEnrolment.Quality;
using Biolog.Features.FaceVerification.Domain;
using Biolog.Features.FaceVerification.Service;
using Xunit;

namespace Biolog.Features.FaceVerification.Tests;

/// <summary>
/// BIO-BIO-005-QA-01 (Chioma acceptance, High): an acceptance probe reproduced
/// <see cref="FaceVerificationService.CompareAsync"/> silently returning
/// <see cref="FaceComparisonOutcome.Matched"/> against a reference derived from a photo master
/// version that was no longer the claimed identity's current one, with no defined reject
/// outcome. These tests reproduce that exact scenario and prove the fix.
/// </summary>
public sealed class FaceVerificationPhotoLifecycleTests
{
    private const string NowUtc = "2026-01-01T00:00:00Z";
    private static readonly byte[] Payload = [1, 0, 9, 9, 9];

    private static FaceVerificationService NewService(FakeFaceReferenceGateway gateway) => new(
        gateway, new SyntheticFaceProvider(), new SyntheticBiometricMatcher(), new SyntheticFaceQualityChecker(),
        new SyntheticFaceVerificationThresholdPolicy(), new InMemoryFaceVerificationEvidenceStore(),
        newId: () => "evidence-1", nowUtc: () => NowUtc);

    private static BiometricSample Sample() => new() { Payload = Payload, ContentType = "application/octet-stream", CapturedAtUtc = NowUtc };

    [Fact]
    public async Task A_reference_from_a_superseded_photo_is_recorded_as_ReferenceStale_not_Matched()
    {
        var gateway = new FakeFaceReferenceGateway()
            .Add("ten_a", "per_a", Payload, sourcePhotoMasterVersion: 1)
            .SetCurrentPhotoMasterVersion("ten_a", "per_a", 2); // the photo was updated to v2 after the reference was captured

        var evidence = await NewService(gateway).CompareAsync("ten_a", "per_a", Sample(), CancellationToken.None);

        Assert.Equal(FaceComparisonOutcome.ReferenceStale, evidence.ComparisonOutcome);
        Assert.Equal(1, evidence.SourcePhotoMasterVersion);
        Assert.Equal(2, evidence.CurrentPhotoMasterVersion);
        Assert.Null(evidence.Score);
        Assert.Null(evidence.MatcherId);
    }

    [Fact]
    public async Task A_stale_reference_deterministically_denies_authorization_with_an_explicit_reason()
    {
        var gateway = new FakeFaceReferenceGateway()
            .Add("ten_a", "per_a", Payload, sourcePhotoMasterVersion: 1)
            .SetCurrentPhotoMasterVersion("ten_a", "per_a", 2);

        var evidence = await NewService(gateway).CompareAsync("ten_a", "per_a", Sample(), CancellationToken.None);
        var decision = FaceAuthorizationPolicy.Decide(evidence);

        Assert.Equal(AccessDecision.Deny, decision.Decision);
        Assert.Equal("reference_stale", decision.DenyReason);
    }

    [Fact]
    public async Task An_identity_with_no_current_photo_master_at_all_is_also_stale()
    {
        var gateway = new FakeFaceReferenceGateway()
            .Add("ten_a", "per_a", Payload, sourcePhotoMasterVersion: 1)
            .SetCurrentPhotoMasterVersion("ten_a", "per_a", null);

        var evidence = await NewService(gateway).CompareAsync("ten_a", "per_a", Sample(), CancellationToken.None);

        Assert.Equal(FaceComparisonOutcome.ReferenceStale, evidence.ComparisonOutcome);
        Assert.Null(evidence.CurrentPhotoMasterVersion);
    }

    [Fact]
    public async Task A_reference_whose_source_photo_is_still_current_matches_normally()
    {
        // Default Add() behaviour: current photo master version == the reference's own source
        // version, i.e. not stale - the ordinary happy path must be unaffected by this fix.
        var gateway = new FakeFaceReferenceGateway().Add("ten_a", "per_a", Payload, sourcePhotoMasterVersion: 1);

        var evidence = await NewService(gateway).CompareAsync("ten_a", "per_a", Sample(), CancellationToken.None);

        Assert.Equal(FaceComparisonOutcome.Matched, evidence.ComparisonOutcome);
        Assert.Equal(1, evidence.CurrentPhotoMasterVersion);
    }

    [Fact]
    public async Task Staleness_is_detected_before_extraction_is_ever_attempted()
    {
        // An empty payload would normally trigger AcquisitionFailed once extraction is attempted
        // - but staleness must be checked FIRST, so a stale+garbage combination still reports
        // ReferenceStale, proving extraction never ran against a superseded reference at all.
        var gateway = new FakeFaceReferenceGateway()
            .Add("ten_a", "per_a", Payload, sourcePhotoMasterVersion: 1)
            .SetCurrentPhotoMasterVersion("ten_a", "per_a", 2);

        var evidence = await NewService(gateway).CompareAsync(
            "ten_a", "per_a", new BiometricSample { Payload = [], ContentType = "application/octet-stream", CapturedAtUtc = NowUtc },
            CancellationToken.None);

        Assert.Equal(FaceComparisonOutcome.ReferenceStale, evidence.ComparisonOutcome);
    }
}

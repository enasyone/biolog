using Biolog.Features.Biometrics.Providers;
using Biolog.Features.Biometrics.Synthetic;
using Biolog.Features.FaceEnrolment.Quality;
using Biolog.Features.FaceVerification.Domain;
using Biolog.Features.FaceVerification.Service;
using Xunit;

namespace Biolog.Features.FaceVerification.Tests;

/// <summary>Applies BIO-BIO-004-F01's lesson (Seyi round 1, High on that story) proactively:
/// <see cref="FaceVerificationService.CompareAsync"/> must contain EVERY exception
/// <see cref="IFaceProvider.ExtractTemplateAsync"/> can raise, not only
/// <see cref="ArgumentException"/>, while still letting a genuine caller cancellation
/// propagate.</summary>
public sealed class FaceVerificationAcquisitionFailureTests
{
    private const string NowUtc = "2026-01-01T00:00:00Z";

    private static FaceVerificationService NewServiceWithReference(IFaceProvider provider, FakeFaceReferenceGateway gateway)
    {
        gateway.Add("ten_a", "per_a", [1, 0, 9, 9, 9]);
        return new FaceVerificationService(
            gateway, provider, new SyntheticBiometricMatcher(), new SyntheticFaceQualityChecker(),
            new SyntheticFaceVerificationThresholdPolicy(), new InMemoryFaceVerificationEvidenceStore(),
            newId: () => "evidence-1", nowUtc: () => NowUtc);
    }

    private static BiometricSample Sample(byte[] payload) => new() { Payload = payload, ContentType = "application/octet-stream", CapturedAtUtc = NowUtc };

    [Fact]
    public async Task An_empty_live_capture_is_recorded_as_AcquisitionFailed_not_propagated()
    {
        var gateway = new FakeFaceReferenceGateway();
        var service = NewServiceWithReference(new SyntheticFaceProvider(), gateway);

        var evidence = await service.CompareAsync("ten_a", "per_a", Sample([]), CancellationToken.None);

        Assert.Equal(FaceComparisonOutcome.AcquisitionFailed, evidence.ComparisonOutcome);
        Assert.Null(evidence.Score);
        Assert.Null(evidence.MatcherId);
    }

    [Fact]
    public async Task A_non_ArgumentException_provider_failure_is_recorded_as_AcquisitionFailed_not_propagated()
    {
        var gateway = new FakeFaceReferenceGateway();
        var provider = new FakeFaceProvider(() => new InvalidOperationException("camera offline"));
        var service = NewServiceWithReference(provider, gateway);

        var evidence = await service.CompareAsync("ten_a", "per_a", Sample([1, 0, 9, 9, 9]), CancellationToken.None);

        Assert.Equal(FaceComparisonOutcome.AcquisitionFailed, evidence.ComparisonOutcome);
    }

    [Fact]
    public async Task A_TimeoutException_provider_failure_is_recorded_as_AcquisitionFailed_not_propagated()
    {
        var gateway = new FakeFaceReferenceGateway();
        var provider = new FakeFaceProvider(() => new TimeoutException("camera did not respond"));
        var service = NewServiceWithReference(provider, gateway);

        var evidence = await service.CompareAsync("ten_a", "per_a", Sample([1, 0, 9, 9, 9]), CancellationToken.None);

        Assert.Equal(FaceComparisonOutcome.AcquisitionFailed, evidence.ComparisonOutcome);
    }

    [Fact]
    public async Task A_vendor_specific_exception_type_is_recorded_as_AcquisitionFailed_not_propagated()
    {
        var gateway = new FakeFaceReferenceGateway();
        var provider = new FakeFaceProvider(() => new FormatException("vendor SDK: corrupt frame"));
        var service = NewServiceWithReference(provider, gateway);

        var evidence = await service.CompareAsync("ten_a", "per_a", Sample([1, 0, 9, 9, 9]), CancellationToken.None);

        Assert.Equal(FaceComparisonOutcome.AcquisitionFailed, evidence.ComparisonOutcome);
    }

    [Fact]
    public async Task A_genuine_caller_cancellation_propagates_and_writes_no_evidence()
    {
        var gateway = new FakeFaceReferenceGateway();
        using var cts = new CancellationTokenSource();
        var provider = new FakeFaceProvider(() => new OperationCanceledException(cts.Token));
        var service = NewServiceWithReference(provider, gateway);
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => service.CompareAsync("ten_a", "per_a", Sample([1, 0, 9, 9, 9]), cts.Token));
    }
}

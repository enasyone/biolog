using Biolog.Features.Biometrics.Providers;
using Biolog.Features.FaceEnrolment.Liveness;
using Xunit;

namespace Biolog.Features.FaceEnrolment.Tests;

public sealed class SyntheticLivenessDetectorTests
{
    private static BiometricSample Sample(byte[] payload) => new()
    {
        Payload = payload,
        ContentType = "application/octet-stream",
        CapturedAtUtc = "2026-01-01T00:00:00Z",
    };

    [Fact]
    public async Task AC05_result_names_a_provider_id_without_the_caller_naming_a_vendor()
    {
        ILivenessDetector detector = new SyntheticLivenessDetector();
        var result = await detector.DetectAsync(Sample([1, 2, 3]), CancellationToken.None);
        Assert.Equal(detector.ProviderId, result.ProviderId);
    }

    [Fact]
    public async Task Result_is_deterministic_for_identical_input()
    {
        var detector = new SyntheticLivenessDetector();
        var first = await detector.DetectAsync(Sample([9, 9, 9]), CancellationToken.None);
        var second = await detector.DetectAsync(Sample([9, 9, 9]), CancellationToken.None);
        Assert.Equal(first.IsLive, second.IsLive);
    }

    [Fact]
    public async Task An_empty_payload_is_never_live()
    {
        var detector = new SyntheticLivenessDetector();
        var result = await detector.DetectAsync(Sample([]), CancellationToken.None);
        Assert.False(result.IsLive);
    }
}

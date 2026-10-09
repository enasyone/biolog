using Biolog.Features.Biometrics.Providers;
using Biolog.Features.FingerprintEnrolment.Quality;
using Xunit;

namespace Biolog.Features.FingerprintEnrolment.Tests;

public sealed class SyntheticFingerQualityScorerTests
{
    private static BiometricSample Sample(byte[] payload) => new()
    {
        Payload = payload,
        ContentType = "application/octet-stream",
        CapturedAtUtc = "2026-01-01T00:00:00Z",
    };

    [Fact]
    public async Task Score_is_always_within_0_to_100()
    {
        var scorer = new SyntheticFingerQualityScorer();

        var low = await scorer.ScoreAsync(Sample([0, 0, 0]), CancellationToken.None);
        var high = await scorer.ScoreAsync(Sample([255, 255, 255]), CancellationToken.None);

        Assert.InRange(low, 0, 100);
        Assert.InRange(high, 0, 100);
        Assert.True(high > low);
    }

    [Fact]
    public async Task Score_is_deterministic_for_identical_input()
    {
        var scorer = new SyntheticFingerQualityScorer();

        var first = await scorer.ScoreAsync(Sample([10, 20, 30]), CancellationToken.None);
        var second = await scorer.ScoreAsync(Sample([10, 20, 30]), CancellationToken.None);

        Assert.Equal(first, second);
    }
}

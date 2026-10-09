using Biolog.Features.Biometrics.Providers;

namespace Biolog.Features.FingerprintEnrolment.Quality;

/// <summary>
/// Deterministic, offline-safe stand-in <see cref="IFingerQualityScorer"/> - no real fingerprint
/// SDK or quality metric, consistent with BIO-BIO-001-AC05's "no real biometric data required"
/// precedent this story follows (its own top-level constraints: "use synthetic data only").
/// Scores by mapping the sample payload's mean byte value onto 0-100 - enough to make richer
/// (larger/more varied) synthetic samples score differently from thin ones for test purposes,
/// with NO accuracy claim of any kind.
/// </summary>
public sealed class SyntheticFingerQualityScorer : IFingerQualityScorer
{
    public Task<int> ScoreAsync(BiometricSample sample, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sample);
        var payload = sample.Payload;
        if (payload.Length == 0)
        {
            return Task.FromResult(0);
        }

        long sum = 0;
        foreach (var b in payload)
        {
            sum += b;
        }
        var mean = (double)sum / payload.Length;
        var score = (int)Math.Round(mean / 255.0 * 100.0, MidpointRounding.AwayFromZero);
        return Task.FromResult(Math.Clamp(score, 0, 100));
    }
}

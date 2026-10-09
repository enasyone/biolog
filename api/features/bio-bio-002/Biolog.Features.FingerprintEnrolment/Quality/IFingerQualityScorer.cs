using Biolog.Features.Biometrics.Providers;

namespace Biolog.Features.FingerprintEnrolment.Quality;

/// <summary>BIO-BIO-002-AC02: "quality [is] recorded." The actual scoring ALGORITHM lives behind
/// this seam, not in <see cref="Service.FingerprintEnrolmentService"/> - the same
/// interface-first separation BIO-BIO-001-AC03 established for matching. This story ships only
/// <see cref="SyntheticFingerQualityScorer"/>; a later story can implement this against a real
/// vendor SDK's quality metric without the service changing.</summary>
public interface IFingerQualityScorer
{
    /// <summary>A score from 0 (unusable) to 100 (excellent), deterministic for identical input.</summary>
    Task<int> ScoreAsync(BiometricSample sample, CancellationToken cancellationToken);
}

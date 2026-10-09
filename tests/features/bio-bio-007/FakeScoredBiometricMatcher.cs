using Biolog.Features.Biometrics.Domain;
using Biolog.Features.Biometrics.Matching;

namespace Biolog.Features.FaceSearch.Tests;

/// <summary>A matcher that returns a per-candidate-person configured score, regardless of the
/// templates' actual bytes. <see cref="Biolog.Features.Biometrics.Synthetic.SyntheticBiometricMatcher"/>
/// only ever scores exactly 0.0 or 1.0 (exact-byte equality), which cannot demonstrate a
/// meaningful multi-candidate RANKING (AC01) - this fake lets tests assign distinct scores to
/// distinct gallery members and assert the resulting order.</summary>
public sealed class FakeScoredBiometricMatcher(Dictionary<string, double> scoresByPersonId) : IBiometricMatcher
{
    public string MatcherId => "fake-scored-v1";

    public Task<BiometricMatchResult> CompareAsync(BiometricTemplate probe, BiometricTemplate candidate, CancellationToken cancellationToken)
    {
        var score = scoresByPersonId.GetValueOrDefault(candidate.PersonId, 0.0);
        return Task.FromResult(new BiometricMatchResult { IsMatch = score >= 0.5, Score = score, MatcherId = MatcherId, MatchedTemplateId = candidate.Id });
    }
}

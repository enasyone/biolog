using Biolog.Features.Biometrics.Domain;
using Biolog.Features.Biometrics.Matching;

namespace Biolog.Features.FaceVerification.Tests;

/// <summary>A matcher that always returns a fixed <see cref="BiometricMatchResult.Score"/>,
/// regardless of the two templates compared. <see cref="SyntheticBiometricMatcher"/> only ever
/// scores exactly 0.0 or 1.0 (exact-byte equality), which cannot demonstrate AC02's
/// threshold-driven branching at a boundary (the maximum allowed threshold, 1.0, can never
/// reject a genuine 1.0 match) - this fake lets tests prove the SAME score is accepted under one
/// threshold and rejected under another.</summary>
public sealed class FakeBiometricMatcher(double score) : IBiometricMatcher
{
    public string MatcherId => "fake-fixed-score-v1";

    public Task<BiometricMatchResult> CompareAsync(BiometricTemplate probe, BiometricTemplate candidate, CancellationToken cancellationToken) =>
        Task.FromResult(new BiometricMatchResult { IsMatch = score >= 0.5, Score = score, MatcherId = MatcherId, MatchedTemplateId = candidate.Id });
}

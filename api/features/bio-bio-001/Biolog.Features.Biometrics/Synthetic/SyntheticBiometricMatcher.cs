using Biolog.Features.Biometrics.Domain;
using Biolog.Features.Biometrics.Matching;

namespace Biolog.Features.Biometrics.Synthetic;

/// <summary>
/// BIO-BIO-001-AC05/AC03: a deterministic, offline-safe stand-in <see cref="IBiometricMatcher"/>.
/// Because <see cref="SyntheticFingerprintProvider"/>/<see cref="SyntheticFaceProvider"/> extract
/// a SHA-256 digest of the raw sample, this matcher can score two templates by exact-byte
/// equality (a genuine re-presentation of the identical synthetic sample hashes identically; any
/// different sample hashes differently) without needing a real biometric similarity algorithm.
/// This makes NO accuracy claim - it is not a false-accept/false-reject-rate-tested matcher, only
/// a way to prove <see cref="Service.BiometricService"/>'s enrol/verify orchestration works
/// end-to-end without a vendor SDK. A later story implements <see cref="IBiometricMatcher"/>
/// with a real similarity algorithm.
/// </summary>
public sealed class SyntheticBiometricMatcher : IBiometricMatcher
{
    public string MatcherId => "synthetic-exact-match-v1";

    public Task<BiometricMatchResult> CompareAsync(
        BiometricTemplate probe, BiometricTemplate candidate, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentNullException.ThrowIfNull(candidate);

        var isMatch = probe.Modality == candidate.Modality && probe.Payload.AsSpan().SequenceEqual(candidate.Payload);
        return Task.FromResult(new BiometricMatchResult
        {
            IsMatch = isMatch,
            Score = isMatch ? 1.0 : 0.0,
            MatcherId = MatcherId,
            MatchedTemplateId = candidate.Id,
        });
    }
}

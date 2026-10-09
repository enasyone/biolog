using Biolog.Features.Biometrics.Domain;

namespace Biolog.Features.Biometrics.Matching;

/// <summary>
/// BIO-BIO-001-AC03: the actual comparison ALGORITHM never lives in
/// <see cref="Service.BiometricService"/> - it lives behind this seam. This story ships only
/// <see cref="Synthetic.SyntheticBiometricMatcher"/>, a deterministic payload-distance stub for
/// local development (AC05) that makes no accuracy claim whatsoever; a later story implements
/// real 1:N/1:1 biometric matching (vendor SDK or a MySQL-backed search) against this interface
/// without the service's orchestration code changing.
/// </summary>
public interface IBiometricMatcher
{
    /// <summary>Stable identifier for this matcher implementation, carried onto
    /// <see cref="BiometricMatchResult.MatcherId"/> so a caller knows which algorithm produced a
    /// result.</summary>
    string MatcherId { get; }

    Task<BiometricMatchResult> CompareAsync(
        BiometricTemplate probe, BiometricTemplate candidate, CancellationToken cancellationToken);
}

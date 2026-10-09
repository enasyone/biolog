using System.Collections.Concurrent;

namespace Biolog.Features.FingerprintSearch.Domain;

/// <summary>An in-process, in-memory <see cref="IFingerprintDuplicateThresholdPolicy"/> for local
/// development and this story's own tests - explicitly NOT a real policy-administration layer,
/// the same scope note BIO-BIO-005's own <c>SyntheticFaceVerificationThresholdPolicy</c>
/// documents. <see cref="SetThresholdForTenant"/> is a configuration-time-only operation
/// <see cref="Service.FingerprintSearchService"/> never calls itself.</summary>
public sealed class SyntheticFingerprintDuplicateThresholdPolicy : IFingerprintDuplicateThresholdPolicy
{
    /// <summary>Matches <see cref="Biolog.Features.Biometrics.Synthetic.SyntheticBiometricMatcher"/>'s
    /// own binary 1.0/0.0 exact-match score.</summary>
    public const double DefaultThreshold = 0.8;

    private readonly ConcurrentDictionary<string, double> _overridesByTenant = new(StringComparer.Ordinal);

    public double GetThreshold(string tenantId) => _overridesByTenant.TryGetValue(tenantId, out var threshold) ? threshold : DefaultThreshold;

    public void SetThresholdForTenant(string tenantId, double threshold)
    {
        if (threshold is < 0.0 or > 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(threshold), threshold, "a similarity threshold must be within [0.0, 1.0].");
        }
        _overridesByTenant[tenantId] = threshold;
    }
}

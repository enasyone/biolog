using System.Collections.Concurrent;

namespace Biolog.Features.FaceVerification.Domain;

/// <summary>
/// AC02/AC05 (BIO-BIO-001's own "no real biometric data required locally"): an in-process,
/// in-memory <see cref="IFaceVerificationThresholdPolicy"/> for local development and this
/// story's own tests - explicitly NOT a real policy-administration layer. <see cref="DefaultThreshold"/>
/// applies to every tenant unless <see cref="SetThresholdForTenant"/> overrides it - a
/// configuration-time operation (called by an operator/administration channel, never by an
/// ordinary verification caller; <see cref="Service.FaceVerificationService.CompareAsync"/> never
/// calls it) - the same "ordinary caller has no path to influence this value" property AC02 asks
/// for, regardless of how many tenants configure their own override. A later story implementing
/// a real, persisted, RBAC-gated policy administration surface implements this interface without
/// <see cref="Service.FaceVerificationService"/> changing shape.
/// </summary>
public sealed class SyntheticFaceVerificationThresholdPolicy : IFaceVerificationThresholdPolicy
{
    /// <summary>Matches <see cref="Biolog.Features.Biometrics.Synthetic.SyntheticBiometricMatcher"/>'s
    /// own binary 1.0/0.0 exact-match score - any threshold in (0, 1] draws the same line the
    /// matcher's own <c>IsMatch</c> already draws for this synthetic implementation, so the two
    /// never disagree with each other purely by coincidence of the default's value.</summary>
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

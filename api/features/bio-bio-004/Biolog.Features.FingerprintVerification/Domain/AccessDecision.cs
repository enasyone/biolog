namespace Biolog.Features.FingerprintVerification.Domain;

/// <summary>
/// AC04: "Suspended/revoked identities cannot be converted into access allow by match alone."
/// The final outcome a caller acts on - deliberately a SEPARATE concept from
/// <see cref="BiometricComparisonOutcome"/>: a <see cref="BiometricComparisonOutcome.Matched"/>
/// comparison only ever produces <see cref="Allow"/> when the claimed identity's status also
/// permits it (see <see cref="Service.FingerprintVerificationService"/>). A caller that only
/// inspects <see cref="AccessDecision"/> can never observe a bare biometric match as access.
/// </summary>
public enum AccessDecision
{
    Deny,
    Allow,
}

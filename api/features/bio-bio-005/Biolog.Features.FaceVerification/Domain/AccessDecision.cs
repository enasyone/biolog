namespace Biolog.Features.FaceVerification.Domain;

/// <summary>The outcome of <see cref="FaceAuthorizationPolicy.Decide"/> - deliberately never a
/// field on <see cref="FaceVerificationEvidence"/> itself (AC03). Mirrors BIO-BIO-004's
/// <c>AccessDecision</c> naming/shape for vocabulary consistency across the BIO-BIO epic.</summary>
public enum AccessDecision
{
    Deny,
    Allow,
}

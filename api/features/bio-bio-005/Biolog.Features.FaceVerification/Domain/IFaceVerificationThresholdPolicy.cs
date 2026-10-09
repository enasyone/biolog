namespace Biolog.Features.FaceVerification.Domain;

/// <summary>
/// AC02: "Threshold is policy-based rather than arbitrary UI input for ordinary users."
/// <see cref="Service.FaceVerificationService.CompareAsync"/> has NO threshold parameter in its
/// signature at all - an ordinary caller cannot pass one no matter what a UI/API layer above it
/// might otherwise be tempted to forward. The threshold used for every comparison is always
/// resolved from here, keyed only by tenant (never by an individual request), the same
/// "narrow interface this feature owns; the host wires a concrete implementation" shape
/// <see cref="IFaceReferenceGateway"/> uses for its own seam.
/// </summary>
public interface IFaceVerificationThresholdPolicy
{
    double GetThreshold(string tenantId);
}

namespace Biolog.Features.AccessDecisions.Domain;

/// <summary>
/// Cross-story seam onto BIO-BIO-005's own already-registered <c>FaceVerificationService.CompareAsync</c>.
/// Never throws for "no reference enrolled" - the adapter catches BIO-BIO-005's own
/// <c>FaceVerificationException</c> and reports <see cref="BiometricComparisonOutcome.NotEnrolled"/>
/// instead (see that outcome's own header on <see cref="BiometricComparisonOutcome"/>).
/// </summary>
public interface IFaceEvidenceGateway
{
    Task<BiometricEvidenceEntry> VerifyAsync(string personId, BiometricSampleInput sample, CancellationToken cancellationToken = default);
}

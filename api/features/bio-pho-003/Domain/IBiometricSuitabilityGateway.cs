namespace Biolog.Features.PhotoQuality.Domain;

/// <summary>
/// This feature's own narrow seam for "is this candidate photo biometrically suitable" -
/// deliberately NOT a <c>ProjectReference</c> to <c>api/features/bio-bio-003</c>. The real
/// implementation is an adapter in <c>Biolog.Api.Modules.BioPho003Module</c> wrapping
/// BIO-BIO-003's own already-registered, PUBLIC <c>IFaceQualityChecker.AssessAsync</c> and
/// <c>ILivenessDetector.DetectAsync</c> directly - a real face-count/seven-check quality
/// assessment plus a real liveness verdict, never a feature-local re-implementation of either.
/// Read-only: unlike BIO-BIO-003's own <c>FaceEnrolmentService.CaptureAsync</c>, this gateway
/// never commits a <c>FaceBiometricReference</c> - that remains entirely BIO-BIO-003's own
/// write scope, untouched by this story (see docs/features/bio-pho-003/README.md "Deliberately
/// out of scope" for the explicit boundary).
/// </summary>
public interface IBiometricSuitabilityGateway
{
    Task<BiometricSuitabilityResult> AssessAsync(byte[] content, CancellationToken cancellationToken = default);
}

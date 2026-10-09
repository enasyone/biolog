using Biolog.Features.Biometrics.Domain;

namespace Biolog.Features.FaceSearch.Domain;

/// <summary>
/// BIO-BIO-007-QA-01 (Chioma acceptance, High). The user story's own wording is "search against
/// APPROVED tenant populations" - not "search against whatever raw sample a caller hands in."
/// This is the seam that makes "approved" real: the only way an identity enters the searchable
/// gallery is through the claimed identity's CURRENT approved BIO-BIO-003 face reference,
/// resolved through this narrow gateway - deliberately NOT a direct call to BIO-BIO-003's own
/// <c>FaceEnrolmentService.GetLatestReferenceAsync</c> (that service is internal and
/// Scoped-registered, the same tenant-scoped-aggregate shape BIO-BIO-005's own
/// <c>IFaceReferenceGateway</c> already documents this exact reasoning for). The real
/// implementation is an adapter in <c>Biolog.Api.Modules.BioBio007Module</c> wrapping that
/// already-registered service directly.
///
/// <b>What "approved" means here.</b> BIO-BIO-003's own <c>FaceEnrolmentService.CaptureAsync</c>
/// only ever constructs a <c>FaceBiometricReference</c> for a capture that passed BOTH its
/// seven-check quality gate AND its liveness gate (BIO-BIO-003-AC01/AC02/AC05) - a rejected
/// capture never produces one at all. Resolving THIS gateway's result is therefore sufficient
/// proof of approval; <see cref="Service.FaceSearchService.IndexAsync"/> never re-derives a
/// template from a caller-supplied raw sample the way the original, rejected implementation did.
/// </summary>
public interface IFaceGalleryApprovalGateway
{
    /// <summary>The claimed identity's CURRENT approved reference template, or <c>null</c> if
    /// none exists - a genuine "nothing approved to index" outcome, never a raw fallback.</summary>
    Task<BiometricTemplate?> GetLatestApprovedReferenceAsync(string tenantId, string personId, CancellationToken cancellationToken = default);
}

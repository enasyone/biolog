using Biolog.Features.Biometrics.Domain;

namespace Biolog.Features.FaceVerification.Domain;

/// <summary>
/// AC04's own seam for "resolve the claimed identity's CURRENT authorised face reference and
/// current photo lifecycle state" - deliberately NOT a direct call to BIO-BIO-003's own
/// <c>FaceEnrolmentService.GetLatestReferenceAsync</c>/BIO-PHO-001's own
/// <c>PhotoMasterService.GetLatestAsync</c> (see this project's csproj remarks for why: both are
/// internal/tenant-scoped Scoped-registered services). The real implementation is an adapter in
/// <c>Biolog.Api.Modules.BioBio005Module</c> wrapping both already-registered services directly.
///
/// <b>AC04 - "reference/photo lifecycle changes are handled deterministically".</b>
/// <see cref="GetLatestApprovedReferenceAsync"/> has exactly one query - "the latest one" - never
/// a query parameterised by a specific reference id/version a caller could pin to something
/// stale; a re-capture (a new approved reference superseding an old one) is picked up
/// automatically on the very next verification attempt with no cache to invalidate.
///
/// <b>BIO-BIO-005-QA-01 (Chioma acceptance, High).</b> Resolving the latest REFERENCE alone is
/// only half of AC04's "reference/PHOTO lifecycle" wording: a reference stays the latest one even
/// after the photo it was derived from has itself been superseded by a newer
/// <c>PhotoMaster</c> (BIO-PHO-001 allows appending a new photo without forcing a face
/// re-capture) - an acceptance probe reproduced <see cref="Service.FaceVerificationService"/>
/// silently matching a live capture against a reference derived from a photo that was no longer
/// current, with no defined reject/invalidate/re-enrol outcome. <see cref="GetCurrentPhotoMasterVersionAsync"/>
/// closes that gap: the service compares its result against
/// <see cref="FaceReferenceSnapshot.SourcePhotoMasterVersion"/> on every call and records
/// <see cref="FaceComparisonOutcome.ReferenceStale"/> - a defined, deterministic, always-recorded
/// outcome - instead of proceeding to a biometric comparison whose result would be meaningless
/// against a superseded photo.
/// </summary>
public interface IFaceReferenceGateway
{
    Task<FaceReferenceSnapshot?> GetLatestApprovedReferenceAsync(string tenantId, string personId, CancellationToken cancellationToken = default);

    /// <summary>The claimed identity's CURRENT <c>PhotoMaster</c> version, or <c>null</c> if the
    /// person has no photo master at all (an identity that was never issued a photo cannot have a
    /// current one to compare against, so a resolved reference is stale by definition in that
    /// case too - see <see cref="Service.FaceVerificationService"/>'s remarks).</summary>
    Task<int?> GetCurrentPhotoMasterVersionAsync(string tenantId, string personId, CancellationToken cancellationToken = default);
}

/// <summary>A read-only projection of BIO-BIO-003's own <c>FaceBiometricReference</c> - carries
/// <see cref="Template"/> (BIO-BIO-001's own canonical <see cref="BiometricTemplate"/>, reused
/// directly rather than re-derived) plus the two identifying facts this feature needs
/// (<see cref="ReferenceId"/>, <see cref="SourcePhotoMasterVersion"/>). Never a full copy of
/// BIO-BIO-003's own internal type.</summary>
public sealed record FaceReferenceSnapshot
{
    public required string ReferenceId { get; init; }
    public required BiometricTemplate Template { get; init; }
    public required int SourcePhotoMasterVersion { get; init; }
}

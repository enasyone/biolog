namespace Biolog.Features.FaceEnrolment.Domain;

/// <summary>
/// This feature's own narrow seam for "does this (person, photo master version) exist in the
/// resolved tenant" - deliberately NOT a <c>ProjectReference</c> to <c>api/features/bio-pho-001</c>
/// (this project's csproj remarks explain why). The real implementation is an adapter in
/// <c>Biolog.Api.Modules.BioBio003Module</c> wrapping BIO-PHO-001's own already-registered
/// <c>PhotoMasterService.GetVersionAsync</c> - the same shape BIO-PHO-001/BIO-PHO-002/BIO-IDN-002
/// through 006 already use for their own <c>IPersonExistenceGateway</c>.
/// </summary>
public interface IPhotoMasterExistenceGateway
{
    Task<bool> ExistsAsync(string personId, int photoMasterVersion, CancellationToken cancellationToken = default);
}

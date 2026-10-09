namespace Biolog.Features.CardIssuance.Domain;

/// <summary>
/// This feature's own narrow seam for "does PersonId exist in the resolved tenant" -
/// deliberately NOT a <c>ProjectReference</c> to <c>api/features/bio-idn-001</c> (this
/// project's csproj remarks explain why: no feature project references another). The real
/// implementation is an adapter in <c>Biolog.Api.Modules.BioCrd005Module</c> wrapping
/// BIO-IDN-001's own already-registered <c>PersonService.GetAsync</c> - tenant-scoping for
/// free, the same shape BIO-PHO-001's own identical interface already established.
/// </summary>
public interface IPersonExistenceGateway
{
    Task<bool> ExistsAsync(string personId, CancellationToken cancellationToken = default);
}

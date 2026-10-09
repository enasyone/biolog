namespace Biolog.Features.Photography.Domain;

/// <summary>
/// This feature's own narrow seam for "does PersonId exist in the resolved tenant" -
/// deliberately NOT a <c>ProjectReference</c> to <c>api/features/bio-idn-001</c> (this
/// project's csproj remarks explain why: no feature project references another). Applying
/// BIO-IDN-002-QA-01's lesson from the start, the same shape BIO-IDN-004/005/006 already used:
/// the real implementation is an adapter in <c>Biolog.Api.Modules.BioPho001Module</c> wrapping
/// BIO-IDN-001's own already-registered <c>PersonService.GetAsync</c> - tenant-scoping for
/// free, since that service reads the same resolved <see cref="Tenancy.ICurrentTenantProvider"/>
/// this feature does.
/// </summary>
public interface IPersonExistenceGateway
{
    Task<bool> ExistsAsync(string personId, CancellationToken cancellationToken = default);
}

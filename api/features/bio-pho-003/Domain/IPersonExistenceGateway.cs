namespace Biolog.Features.PhotoQuality.Domain;

/// <summary>
/// This feature's own narrow seam for "does PersonId exist in the resolved tenant" -
/// deliberately NOT a <c>ProjectReference</c> to <c>api/features/bio-idn-001</c>. The real
/// implementation is an adapter in <c>Biolog.Api.Modules.BioPho003Module</c> wrapping
/// BIO-IDN-001's own already-registered <c>PersonService.GetAsync</c> - tenant-scoping for
/// free, the same BIO-IDN-002-QA-01-derived shape every sibling story has used since.
/// </summary>
public interface IPersonExistenceGateway
{
    Task<bool> ExistsAsync(string personId, CancellationToken cancellationToken = default);
}

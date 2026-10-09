namespace Biolog.Features.WorkforceAttendance.Domain;

/// <summary>
/// This feature's own narrow, read-only seam onto BIO-IDN-001 (the same shape every
/// identity-wave story establishes for itself - never a <c>ProjectReference</c>). A clock-pair
/// query or an adjustment's <c>person_id</c> must reference a person who genuinely exists in the
/// resolved tenant, the same BIO-IDN-002-QA-01 lesson BIO-ATT-001's own identically-named
/// interface already applies. <c>Biolog.Api</c>'s <c>BioAtt003Module</c> implements this by
/// wrapping BIO-IDN-001's own already-registered <c>PersonService.GetAsync</c> directly.
/// </summary>
public interface IPersonExistenceGateway
{
    Task<bool> ExistsAsync(string personId, CancellationToken cancellationToken = default);
}

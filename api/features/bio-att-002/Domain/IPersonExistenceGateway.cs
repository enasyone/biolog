namespace Biolog.Features.AttendanceSessions.Domain;

/// <summary>
/// This feature's own narrow, read-only seam onto BIO-IDN-001 (never a <c>ProjectReference</c>).
/// A session's roster must reference persons who genuinely exist in the resolved tenant, the same
/// BIO-IDN-002-QA-01 lesson every identity-wave story applies. <c>Biolog.Api</c>'s
/// <c>BioAtt002Module</c> implements this by wrapping BIO-IDN-001's own already-registered
/// <c>PersonService.GetAsync</c> directly.
/// </summary>
public interface IPersonExistenceGateway
{
    Task<bool> ExistsAsync(string personId, CancellationToken cancellationToken = default);
}

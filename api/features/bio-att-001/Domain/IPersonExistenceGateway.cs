namespace Biolog.Features.Attendance.Domain;

/// <summary>
/// This feature's own narrow, read-only seam onto BIO-IDN-001 (the same shape every
/// identity-wave story establishes for itself - never a <c>ProjectReference</c>). A captured
/// event's <c>person_id</c> must reference a person who genuinely exists in the resolved
/// tenant, the same BIO-IDN-002-QA-01 lesson applied from the start. <c>Biolog.Api</c>'s
/// <c>BioAtt001Module</c> implements this by wrapping BIO-IDN-001's own already-registered
/// <c>PersonService.GetAsync</c> directly.
///
/// Deliberately does NOT also check lifecycle status (active/suspended/etc.) - attendance
/// capture records a physical occurrence that already happened; refusing to record it because
/// the person is administratively suspended would silently destroy the very fact a suspension
/// investigation might need. Gating WHO may be granted access on suspension status is
/// BIO-ACC-001's job, not this story's - a disclosed scope boundary, not an oversight.
/// </summary>
public interface IPersonExistenceGateway
{
    Task<bool> ExistsAsync(string personId, CancellationToken cancellationToken = default);
}

namespace Biolog.Features.Visitors.Domain;

/// <summary>
/// This feature's own narrow, read-only seam onto BIO-IDN-001 (the same shape every
/// identity-wave story establishes for itself - never a <c>ProjectReference</c>). Used for both
/// an optional <see cref="VisitorRecord.PersonId"/> link (AC01) and the required
/// <see cref="VisitorRecord.HostPersonId"/> - both must reference a person who genuinely exists
/// in the resolved tenant (BIO-IDN-002-QA-01's lesson, applied from the start).
/// <c>Biolog.Api</c>'s <c>BioVis001Module</c> implements this by wrapping BIO-IDN-001's own
/// already-registered <c>PersonService.GetAsync</c> directly.
///
/// Deliberately does NOT also check lifecycle status (active/suspended/etc.) for either link - a
/// visitor record is a fact capture about who is visiting and who is hosting, not a gate on
/// either person's own current status; the same disclosed scope boundary BIO-ATT-001 recorded
/// for its own <see cref="IPersonExistenceGateway"/>.
/// </summary>
public interface IPersonExistenceGateway
{
    Task<bool> ExistsAsync(string personId, CancellationToken cancellationToken = default);
}

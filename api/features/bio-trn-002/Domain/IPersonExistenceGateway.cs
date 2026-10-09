namespace Biolog.Features.TransportEvents.Domain;

/// <summary>
/// This feature's own narrow, read-only seam onto BIO-IDN-001 (the same shape every
/// identity-wave story establishes for itself - never a <c>ProjectReference</c>). Used for
/// <see cref="TransportEvent.PersonId"/> - must reference a person who genuinely exists in the
/// resolved tenant (BIO-IDN-002-QA-01's lesson). <c>Biolog.Api</c>'s <c>BioTrn002Module</c>
/// implements this by wrapping BIO-IDN-001's own already-registered <c>PersonService.GetAsync</c>
/// directly.
///
/// Deliberately does NOT also check lifecycle status - a board/alight event records a fact that
/// already happened, not a gate on the person's own current identity status; the same disclosed
/// scope boundary BIO-ATT-001/BIO-VIS-001/BIO-TRN-001 recorded for their own person gateways.
/// </summary>
public interface IPersonExistenceGateway
{
    Task<bool> ExistsAsync(string personId, CancellationToken cancellationToken = default);
}

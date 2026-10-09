namespace Biolog.Features.TransportNotifications;

/// <summary>
/// A read-only, unified projection of one bio-trn-002 source fact, already classified into one
/// of this story's own <see cref="TransportNotificationEventType"/> values by
/// <see cref="ITransportEventFeedGateway"/>'s own adapter (never by this project re-deriving
/// bio-trn-002's own presence logic). Carries the RAW fields a source fact may have
/// (<see cref="Location"/>, <see cref="VehicleId"/>) purely so <see cref="TransportNotificationPayloadRedactor"/>
/// has something concrete to prove it drops - nothing here is ever itself delivered or persisted
/// as-is (AC02).
/// </summary>
public sealed record TransportFeedItem
{
    public required string SourceEventId { get; init; }
    public required string PersonId { get; init; }
    public string? RouteId { get; init; }
    public string? VehicleId { get; init; }
    public string? Location { get; init; }
    public required TransportNotificationEventType EventType { get; init; }
    public required DateTimeOffset OccurredAt { get; init; }
}

/// <summary>
/// The cross-story read seam onto bio-trn-002 (AC04 - read-only by construction, this interface
/// has no write member at all, so no implementation of it can ever mutate transport event truth).
/// Implemented by an adapter in <c>Biolog.Api</c>'s <c>BioTrn003Module</c> wrapping bio-trn-002's
/// own <c>ITransportEventRepository.ListForTenantAsync</c> (raw board/alight facts, classified
/// Board/Alight by <c>TransportEventMode</c> and additionally as Exception when every evidence
/// entry is unverified) and <c>ITriggeredNotificationLog.ListForTenantAsync</c> (bio-trn-002's own
/// already-computed missing-alight-exception condition, classified MissedAlight) - never
/// bio-trn-002's own internal <c>TransportPresenceEvaluator</c>, which this project does not
/// duplicate.
/// </summary>
public interface ITransportEventFeedGateway
{
    Task<IReadOnlyList<TransportFeedItem>> ListRecentAsync(string tenantId, CancellationToken cancellationToken = default);
}

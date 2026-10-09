namespace Biolog.Features.TransportEvents.Domain;

/// <summary>AC03's derived, read-time-only classification for one (tenant, person, date) - see <see cref="TransportPresenceEvaluator"/>. Never persisted, never a mutation of any <see cref="TransportEvent"/>.</summary>
public sealed record DerivedTransportPresence
{
    public required string TenantId { get; init; }
    public required string PersonId { get; init; }
    public required string Date { get; init; }
    public required DerivedPresenceStatus Status { get; init; }
    public required IReadOnlyList<string> BasedOnEventIds { get; init; }
    public required string ComputedAt { get; init; }
}

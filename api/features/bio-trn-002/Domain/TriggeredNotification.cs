using Biolog.Features.TransportEvents.Identifiers;

namespace Biolog.Features.TransportEvents.Domain;

/// <summary>
/// AC04 - a real, append-only, readable-back record that a configured condition fired for a real
/// event. This IS the feature-local notification-trigger mechanism itself (see
/// <see cref="TransportNotificationConfig"/>'s own header for why this does not call out to
/// BIO-RPT-001's actual delivery channels) - a future integration point (once BIO-RPT-001 is a
/// declared dependency) would translate this same real record into a real delivered
/// notification, the same "future integration point" shape
/// <c>DomainEventEnvelope.cs</c>'s own header describes for its as-yet-unused producers.
/// </summary>
public sealed record TriggeredNotification
{
    public required string Id { get; init; }
    public required string TenantId { get; init; }
    public required string PersonId { get; init; }
    public required string EventId { get; init; }
    public required string RuleId { get; init; }
    public required string Reason { get; init; }
    public required string TriggeredAt { get; init; }

    internal static TriggeredNotification Create(string tenantId, string personId, string eventId, string ruleId, string reason, string now) => new()
    {
        Id = CanonicalId.NewLocalId(),
        TenantId = tenantId,
        PersonId = personId,
        EventId = eventId,
        RuleId = ruleId,
        Reason = reason,
        TriggeredAt = now,
    };
}

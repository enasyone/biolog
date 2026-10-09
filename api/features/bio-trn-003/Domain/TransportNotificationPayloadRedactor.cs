namespace Biolog.Features.TransportNotifications;

/// <summary>AC02's own minimized, delivered content - deliberately NOT a 1:1 copy of <see cref="TransportFeedItem"/>. Explicit allowlist (ADR-0003 Decision 3's own "redaction is an allowlist, never a denylist" discipline, re-applied here fresh since this project cannot reference BIO-RPT-001's own <c>PayloadAllowlist</c>): <see cref="PersonId"/> (needed so the recipient/operator knows WHO this concerns), <see cref="RouteId"/> and <see cref="EventType"/>/<see cref="OccurredAt"/> (needed to know WHAT happened and WHEN) are carried; the source fact's own free-text <see cref="TransportFeedItem.Location"/> and <see cref="TransportFeedItem.VehicleId"/> are deliberately dropped - a human-readable address/stop label and a specific vehicle are richer, more re-identifying detail than this story's own notification needs to serve its purpose.</summary>
public sealed record TransportNotificationPayload
{
    public required string TenantId { get; init; }
    public required string PersonId { get; init; }
    public string? RouteId { get; init; }
    public required TransportNotificationEventType EventType { get; init; }
    public required DateTimeOffset OccurredAt { get; init; }
}

public static class TransportNotificationPayloadRedactor
{
    public static TransportNotificationPayload Minimize(string tenantId, TransportFeedItem item) => new()
    {
        TenantId = tenantId,
        PersonId = item.PersonId,
        RouteId = item.RouteId,
        EventType = item.EventType,
        OccurredAt = item.OccurredAt,
    };
}

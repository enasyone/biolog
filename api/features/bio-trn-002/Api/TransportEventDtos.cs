using System.Text.Json.Serialization;
using Biolog.Features.TransportEvents.Domain;

namespace Biolog.Features.TransportEvents.Api;

// --- Request DTOs (wire shape only - the services own all parsing/validation) ---

public sealed record TransportEvidenceEntryRequest
{
    [JsonPropertyName("method")]
    public string? Method { get; init; }

    [JsonPropertyName("verified")]
    public bool Verified { get; init; }

    [JsonPropertyName("confidence")]
    public double? Confidence { get; init; }
}

public sealed record CaptureTransportEventRequest
{
    [JsonPropertyName("person_id")]
    public string PersonId { get; init; } = "";

    [JsonPropertyName("route_id")]
    public string RouteId { get; init; } = "";

    [JsonPropertyName("vehicle_id")]
    public string VehicleId { get; init; } = "";

    [JsonPropertyName("stop_id")]
    public string? StopId { get; init; }

    [JsonPropertyName("location")]
    public string? Location { get; init; }

    [JsonPropertyName("mode")]
    public string? Mode { get; init; }

    [JsonPropertyName("evidence")]
    public IReadOnlyList<TransportEvidenceEntryRequest> Evidence { get; init; } = [];

    [JsonPropertyName("occurred_at")]
    public string? OccurredAt { get; init; }

    [JsonPropertyName("idempotency_key")]
    public string? IdempotencyKey { get; init; }
}

public sealed record SetTransportNotificationConfigRequest
{
    [JsonPropertyName("notify_on_missing_alight_exception")]
    public bool NotifyOnMissingAlightException { get; init; }
}

// --- Response DTOs ---

public sealed record TransportEvidenceEntryResponse
{
    [JsonPropertyName("method")]
    public required string Method { get; init; }

    [JsonPropertyName("verified")]
    public required bool Verified { get; init; }

    [JsonPropertyName("confidence")]
    public double? Confidence { get; init; }

    public static TransportEvidenceEntryResponse From(TransportEvidenceEntry entry) => new()
    {
        Method = entry.Method.ToWireString(),
        Verified = entry.Verified,
        Confidence = entry.Confidence,
    };
}

public sealed record TransportEventResponse
{
    [JsonPropertyName("event_id")]
    public required string EventId { get; init; }

    [JsonPropertyName("tenant_id")]
    public required string TenantId { get; init; }

    [JsonPropertyName("person_id")]
    public required string PersonId { get; init; }

    [JsonPropertyName("route_id")]
    public required string RouteId { get; init; }

    [JsonPropertyName("vehicle_id")]
    public required string VehicleId { get; init; }

    [JsonPropertyName("stop_id")]
    public string? StopId { get; init; }

    [JsonPropertyName("location")]
    public string? Location { get; init; }

    [JsonPropertyName("mode")]
    public required string Mode { get; init; }

    [JsonPropertyName("evidence")]
    public required IReadOnlyList<TransportEvidenceEntryResponse> Evidence { get; init; }

    [JsonPropertyName("occurred_at")]
    public required string OccurredAt { get; init; }

    [JsonPropertyName("received_at")]
    public required string ReceivedAt { get; init; }

    [JsonPropertyName("idempotency_key")]
    public required string IdempotencyKey { get; init; }

    public static TransportEventResponse From(TransportEvent e) => new()
    {
        EventId = e.EventId,
        TenantId = e.TenantId,
        PersonId = e.PersonId,
        RouteId = e.RouteId,
        VehicleId = e.VehicleId,
        StopId = e.StopId,
        Location = e.Location,
        Mode = e.Mode.ToWireString(),
        Evidence = [.. e.Evidence.Select(TransportEvidenceEntryResponse.From)],
        OccurredAt = e.OccurredAt,
        ReceivedAt = e.ReceivedAt,
        IdempotencyKey = e.IdempotencyKey,
    };
}

public sealed record TransportEventListResponse
{
    [JsonPropertyName("data")]
    public required IReadOnlyList<TransportEventResponse> Data { get; init; }
}

public sealed record DerivedTransportPresenceResponse
{
    [JsonPropertyName("tenant_id")]
    public required string TenantId { get; init; }

    [JsonPropertyName("person_id")]
    public required string PersonId { get; init; }

    [JsonPropertyName("date")]
    public required string Date { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("based_on_event_ids")]
    public required IReadOnlyList<string> BasedOnEventIds { get; init; }

    [JsonPropertyName("computed_at")]
    public required string ComputedAt { get; init; }

    public static DerivedTransportPresenceResponse From(DerivedTransportPresence presence) => new()
    {
        TenantId = presence.TenantId,
        PersonId = presence.PersonId,
        Date = presence.Date,
        Status = presence.Status.ToWireString(),
        BasedOnEventIds = presence.BasedOnEventIds,
        ComputedAt = presence.ComputedAt,
    };
}

public sealed record TransportNotificationConfigResponse
{
    [JsonPropertyName("tenant_id")]
    public required string TenantId { get; init; }

    [JsonPropertyName("notify_on_missing_alight_exception")]
    public required bool NotifyOnMissingAlightException { get; init; }

    [JsonPropertyName("version")]
    public required int Version { get; init; }

    [JsonPropertyName("updated_at")]
    public required string UpdatedAt { get; init; }

    [JsonPropertyName("updated_by")]
    public required string UpdatedBy { get; init; }

    public static TransportNotificationConfigResponse From(TransportNotificationConfig config) => new()
    {
        TenantId = config.TenantId,
        NotifyOnMissingAlightException = config.NotifyOnMissingAlightException,
        Version = config.Version,
        UpdatedAt = config.UpdatedAt,
        UpdatedBy = config.UpdatedBy,
    };
}

public sealed record TriggeredNotificationResponse
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("person_id")]
    public required string PersonId { get; init; }

    [JsonPropertyName("event_id")]
    public required string EventId { get; init; }

    [JsonPropertyName("rule_id")]
    public required string RuleId { get; init; }

    [JsonPropertyName("reason")]
    public required string Reason { get; init; }

    [JsonPropertyName("triggered_at")]
    public required string TriggeredAt { get; init; }

    public static TriggeredNotificationResponse From(TriggeredNotification n) => new()
    {
        Id = n.Id,
        PersonId = n.PersonId,
        EventId = n.EventId,
        RuleId = n.RuleId,
        Reason = n.Reason,
        TriggeredAt = n.TriggeredAt,
    };
}

public sealed record TriggeredNotificationListResponse
{
    [JsonPropertyName("data")]
    public required IReadOnlyList<TriggeredNotificationResponse> Data { get; init; }
}

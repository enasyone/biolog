using System.Text.Json.Serialization;
using Biolog.Features.PersonLifecycle.Audit;
using Biolog.Features.PersonLifecycle.Domain;

namespace Biolog.Features.PersonLifecycle.Api;

public sealed record PersonLifecycleResponse
{
    [JsonPropertyName("person_id")]
    public required string PersonId { get; init; }

    [JsonPropertyName("tenant_id")]
    public required string TenantId { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("version")]
    public required int Version { get; init; }

    /// <summary>BIO-IDN-004-QA-01 (Seyi round 2): makes the suspension/revocation pending-reconciliation state observable, not just inferable from a prior 503.</summary>
    [JsonPropertyName("revocation_pending")]
    public required bool RevocationPending { get; init; }

    public static PersonLifecycleResponse From(PersonSnapshot snapshot, bool revocationPending) => new()
    {
        PersonId = snapshot.PersonId,
        TenantId = snapshot.TenantId,
        Status = snapshot.Status.ToWireString(),
        Version = snapshot.Version,
        RevocationPending = revocationPending,
    };
}

/// <summary>BIO-IDN-004-AC04: one entry in a person's own lifecycle-event history - proof that it remains traceable after the person reaches ARCHIVED.</summary>
public sealed record LifecycleEventResponse
{
    [JsonPropertyName("event_id")]
    public required string EventId { get; init; }

    [JsonPropertyName("action")]
    public required string Action { get; init; }

    [JsonPropertyName("reason_code")]
    public required string ReasonCode { get; init; }

    [JsonPropertyName("actor_id")]
    public required string ActorId { get; init; }

    [JsonPropertyName("actor_type")]
    public required string ActorType { get; init; }

    [JsonPropertyName("occurred_at")]
    public required string OccurredAt { get; init; }

    [JsonPropertyName("correlation_id")]
    public required string CorrelationId { get; init; }

    [JsonPropertyName("detail")]
    public required IReadOnlyDictionary<string, object?> Detail { get; init; }

    public static LifecycleEventResponse From(PersonLifecycleAuditEvent evt) => new()
    {
        EventId = evt.EventId,
        Action = evt.Action,
        ReasonCode = evt.ReasonCode,
        ActorId = evt.Actor.Id,
        ActorType = evt.Actor.Type.ToWireString(),
        OccurredAt = evt.OccurredAt,
        CorrelationId = evt.CorrelationId,
        Detail = evt.Detail,
    };
}

public sealed record LifecycleEventListResponse
{
    [JsonPropertyName("data")]
    public required IReadOnlyList<LifecycleEventResponse> Data { get; init; }
}

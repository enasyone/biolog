using System.Text.Json.Serialization;
using Biolog.Features.AccessCache.Domain;

namespace Biolog.Features.AccessCache.Api;

public sealed record RevocationEntryResponse
{
    [JsonPropertyName("person_id")]
    public required string PersonId { get; init; }

    [JsonPropertyName("revoked_at")]
    public required string RevokedAt { get; init; }

    public static RevocationEntryResponse From(RevocationEntry entry) => new() { PersonId = entry.PersonId, RevokedAt = entry.RevokedAt };
}

/// <summary><c>cursor</c> is the caller's next <c>since</c> value - the max <c>revoked_at</c> across <paramref name="Items"/>, or the request's own <c>since</c> echoed back unchanged when <paramref name="Items"/> is empty (nothing new, nothing to advance past).</summary>
public sealed record RevocationDeltaResponse
{
    [JsonPropertyName("items")]
    public required IReadOnlyList<RevocationEntryResponse> Items { get; init; }

    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }
}

/// <summary>Wire shape matching the mobile client's presentation-event upload envelope: the frozen canonical-identifier/instant conventions (ADR-0001) plus this feature's own <c>operation</c>-free fields (a presentation event is always an append, never an update/revocation of a prior one).</summary>
public sealed record PresentationEventIntakeRequest
{
    [JsonPropertyName("event_id")]
    public required string EventId { get; init; }

    [JsonPropertyName("tenant_id")]
    public required string TenantId { get; init; }

    [JsonPropertyName("device_id")]
    public required string DeviceId { get; init; }

    [JsonPropertyName("person_id")]
    public required string PersonId { get; init; }

    [JsonPropertyName("outcome")]
    public required string Outcome { get; init; }

    [JsonPropertyName("reason_code")]
    public required string ReasonCode { get; init; }

    [JsonPropertyName("occurred_at")]
    public required string OccurredAt { get; init; }

    [JsonPropertyName("cache_snapshot_at")]
    public string? CacheSnapshotAt { get; init; }

    [JsonPropertyName("stale")]
    public bool Stale { get; init; }
}

public sealed record PresentationEventAckResponse
{
    [JsonPropertyName("event_id")]
    public required string EventId { get; init; }

    [JsonPropertyName("received_at")]
    public required string ReceivedAt { get; init; }

    public static PresentationEventAckResponse From(PresentationEvent presentationEvent) => new()
    {
        EventId = presentationEvent.EventId,
        ReceivedAt = presentationEvent.ReceivedAt,
    };
}

public sealed record PresentationEventResponse
{
    [JsonPropertyName("event_id")]
    public required string EventId { get; init; }

    [JsonPropertyName("person_id")]
    public required string PersonId { get; init; }

    [JsonPropertyName("outcome")]
    public required string Outcome { get; init; }

    [JsonPropertyName("reason_code")]
    public required string ReasonCode { get; init; }

    [JsonPropertyName("occurred_at")]
    public required string OccurredAt { get; init; }

    [JsonPropertyName("stale")]
    public bool Stale { get; init; }

    public static PresentationEventResponse From(PresentationEvent presentationEvent) => new()
    {
        EventId = presentationEvent.EventId,
        PersonId = presentationEvent.PersonId,
        Outcome = presentationEvent.Outcome.ToWireString(),
        ReasonCode = presentationEvent.ReasonCode,
        OccurredAt = presentationEvent.OccurredAt,
        Stale = presentationEvent.Stale,
    };
}

public sealed record PresentationEventListResponse
{
    [JsonPropertyName("items")]
    public required IReadOnlyList<PresentationEventResponse> Items { get; init; }
}

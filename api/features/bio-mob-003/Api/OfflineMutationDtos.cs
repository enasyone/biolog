using System.Text.Json;
using System.Text.Json.Serialization;

namespace Biolog.Features.OfflineSync.Api;

/// <summary>
/// Wire shape matching `mobile/lib/core/offline/offline_mutation.dart`'s
/// `toSyncDeltaJson()` exactly: the frozen `offline-mutation.schema.json` envelope fields
/// (`mutation_id`/`tenant_id`/`device_id`/`aggregate_id`/`base_version`/`idempotency_key`/
/// `occurred_at`/`sync_state`) plus the two local-only extensions the client also sends
/// (`operation`, `payload`). `sync_state` is accepted but ignored - it is the CLIENT's own
/// local queue-state bookkeeping (BIO-MOB-002), meaningless to this server.
/// </summary>
public sealed record OfflineMutationDeltaRequest
{
    [JsonPropertyName("mutation_id")]
    public required string MutationId { get; init; }

    [JsonPropertyName("tenant_id")]
    public required string TenantId { get; init; }

    [JsonPropertyName("device_id")]
    public required string DeviceId { get; init; }

    [JsonPropertyName("aggregate_id")]
    public required string AggregateId { get; init; }

    [JsonPropertyName("base_version")]
    public required int BaseVersion { get; init; }

    [JsonPropertyName("idempotency_key")]
    public required string IdempotencyKey { get; init; }

    [JsonPropertyName("occurred_at")]
    public required string OccurredAt { get; init; }

    [JsonPropertyName("operation")]
    public required string Operation { get; init; }

    [JsonPropertyName("payload")]
    public JsonElement Payload { get; init; }
}

public sealed record OfflineMutationAckResponse
{
    [JsonPropertyName("mutation_id")]
    public required string MutationId { get; init; }

    [JsonPropertyName("aggregate_id")]
    public required string AggregateId { get; init; }

    [JsonPropertyName("version")]
    public required int Version { get; init; }

    [JsonPropertyName("operation")]
    public required string Operation { get; init; }

    public static OfflineMutationAckResponse From(Domain.OfflineMutationAckResult result) => new()
    {
        MutationId = result.MutationId,
        AggregateId = result.AggregateId,
        Version = result.Version,
        Operation = result.Operation,
    };
}

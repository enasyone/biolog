namespace Biolog.Features.OfflineSync.Domain;

/// <summary>The durable result of a successfully-intaken mutation - what a replayed idempotency claim returns verbatim (ADR-0001 §3.4).</summary>
public sealed record OfflineMutationAckResult(string MutationId, string AggregateId, int Version, string Operation);

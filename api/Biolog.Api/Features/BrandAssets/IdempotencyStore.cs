using System.Collections.Concurrent;
using System.Text.Json;

namespace Biolog.Api.Features.BrandAssets;

public enum IdempotencyRecordState
{
    InFlight,
    Done,
}

public sealed record IdempotencyRecord(
    string Fingerprint, IdempotencyRecordState State, int? ResponseStatus, string? ResponseBodyJson);

/// <summary>
/// Write-endpoint idempotency store (ADR-0004 Decision 4.4): <c>claim</c>/<c>complete</c>/
/// <c>release</c>/<c>get</c>, not a plain get/set, so a duplicate concurrent request cannot
/// execute twice.
/// </summary>
public interface IIdempotencyStore
{
    /// <summary>
    /// Atomically creates an IN_FLIGHT record for this scope only if none exists. Returns
    /// <c>Won = true</c> when this call created it; otherwise the existing record.
    /// </summary>
    (bool Won, IdempotencyRecord? Existing) Claim(string tenantId, string operation, string idempotencyKey, string fingerprint);

    void Complete(string tenantId, string operation, string idempotencyKey, int responseStatus, string responseBodyJson);

    void Release(string tenantId, string operation, string idempotencyKey);

    IdempotencyRecord? Get(string tenantId, string operation, string idempotencyKey);
}

/// <summary>
/// In-memory reference store (ADR-0004 Decision 4.4's "reference implementation ...
/// demonstrates a store that satisfies the atomicity requirement"). Local to this feature
/// pending a real, durable store; the 24-hour retention floor the ADR requires does not
/// apply to an in-memory placeholder that does not outlive the process in the first place.
/// </summary>
public sealed class InMemoryIdempotencyStore : IIdempotencyStore
{
    private readonly ConcurrentDictionary<string, IdempotencyRecord> _records = new(StringComparer.Ordinal);

    /// <summary>
    /// ADR-0004 §4.2: the scope key is JSON-array-encoded, not delimiter-joined, so a
    /// caller-controlled `operation` or `idempotencyKey` containing the delimiter can never
    /// alias a different endpoint's or key's record.
    /// </summary>
    private static string ScopeKey(string tenantId, string operation, string idempotencyKey) =>
        JsonSerializer.Serialize(new[] { tenantId, operation, idempotencyKey });

    public (bool Won, IdempotencyRecord? Existing) Claim(
        string tenantId, string operation, string idempotencyKey, string fingerprint)
    {
        var key = ScopeKey(tenantId, operation, idempotencyKey);
        var inFlight = new IdempotencyRecord(fingerprint, IdempotencyRecordState.InFlight, null, null);
        // ConcurrentDictionary.TryAdd is the atomic check-and-set this decision requires -
        // no await between the check and the write.
        if (_records.TryAdd(key, inFlight))
        {
            return (true, null);
        }
        return (false, _records[key]);
    }

    public void Complete(string tenantId, string operation, string idempotencyKey, int responseStatus, string responseBodyJson)
    {
        var key = ScopeKey(tenantId, operation, idempotencyKey);
        _records.AddOrUpdate(
            key,
            _ => throw new InvalidOperationException("Complete called without a prior winning Claim"),
            (_, existing) => existing with
            {
                State = IdempotencyRecordState.Done,
                ResponseStatus = responseStatus,
                ResponseBodyJson = responseBodyJson,
            });
    }

    public void Release(string tenantId, string operation, string idempotencyKey)
    {
        _records.TryRemove(ScopeKey(tenantId, operation, idempotencyKey), out _);
    }

    public IdempotencyRecord? Get(string tenantId, string operation, string idempotencyKey)
    {
        return _records.TryGetValue(ScopeKey(tenantId, operation, idempotencyKey), out var record) ? record : null;
    }
}

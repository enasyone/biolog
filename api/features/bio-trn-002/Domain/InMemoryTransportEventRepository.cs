using System.Collections.Concurrent;

namespace Biolog.Features.TransportEvents.Domain;

/// <summary>Process-lifetime, volatile - documentation precision, not a durability claim.</summary>
public sealed class InMemoryTransportEventRepository : ITransportEventRepository
{
    private readonly ConcurrentDictionary<string, TransportEvent> _byEventId = new();
    private readonly ConcurrentDictionary<(string TenantId, string IdempotencyKey), TransportEvent> _byIdempotencyKey = new();

    public Task<TransportEvent> CreateAsync(TransportEvent record, CancellationToken cancellationToken = default)
    {
        _byEventId[record.EventId] = record;
        _byIdempotencyKey[(record.TenantId, record.IdempotencyKey)] = record;
        return Task.FromResult(record);
    }

    public Task<TransportEvent?> GetByIdempotencyKeyAsync(string tenantId, string idempotencyKey, CancellationToken cancellationToken = default) =>
        Task.FromResult(_byIdempotencyKey.TryGetValue((tenantId, idempotencyKey), out var found) ? found : null);

    public Task<IReadOnlyList<TransportEvent>> ListForPersonOnDateAsync(string tenantId, string personId, string date, CancellationToken cancellationToken = default)
    {
        var results = _byEventId.Values
            .Where(e => e.TenantId == tenantId && e.PersonId == personId && e.OccurredAt.StartsWith(date, StringComparison.Ordinal))
            .OrderBy(e => e.OccurredAt, StringComparer.Ordinal)
            .ToList();
        return Task.FromResult<IReadOnlyList<TransportEvent>>(results);
    }

    public Task<IReadOnlyList<TransportEvent>> ListForTenantAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        var results = _byEventId.Values
            .Where(e => e.TenantId == tenantId)
            .OrderBy(e => e.OccurredAt, StringComparer.Ordinal)
            .ToList();
        return Task.FromResult<IReadOnlyList<TransportEvent>>(results);
    }
}

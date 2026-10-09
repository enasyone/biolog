using System.Collections.Concurrent;

namespace Biolog.Features.AccessDecisions.Domain;

/// <summary>Process-lifetime, volatile - documentation precision, not a durability claim.</summary>
public sealed class InMemoryAccessDecisionEventRepository : IAccessDecisionEventRepository
{
    private readonly ConcurrentDictionary<string, AccessDecisionEvent> _byEventId = new();
    private readonly ConcurrentDictionary<(string TenantId, string IdempotencyKey), AccessDecisionEvent> _byIdempotencyKey = new();

    public Task<AccessDecisionEvent> CreateAsync(AccessDecisionEvent record, CancellationToken cancellationToken = default)
    {
        _byEventId[record.EventId] = record;
        if (record.IdempotencyKey is not null)
        {
            _byIdempotencyKey[(record.TenantId, record.IdempotencyKey)] = record;
        }

        return Task.FromResult(record);
    }

    public Task<AccessDecisionEvent?> GetByIdempotencyKeyAsync(string tenantId, string idempotencyKey, CancellationToken cancellationToken = default) =>
        Task.FromResult(_byIdempotencyKey.TryGetValue((tenantId, idempotencyKey), out var found) ? found : null);

    public Task<IReadOnlyList<AccessDecisionEvent>> ListForPersonAsync(string tenantId, string personId, CancellationToken cancellationToken = default)
    {
        var results = _byEventId.Values
            .Where(e => e.TenantId == tenantId && e.PersonId == personId)
            .OrderBy(e => e.OccurredAt, StringComparer.Ordinal)
            .ToList();
        return Task.FromResult<IReadOnlyList<AccessDecisionEvent>>(results);
    }

    public Task<IReadOnlyList<AccessDecisionEvent>> ListForTenantAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        var results = _byEventId.Values
            .Where(e => e.TenantId == tenantId)
            .OrderBy(e => e.OccurredAt, StringComparer.Ordinal)
            .ToList();
        return Task.FromResult<IReadOnlyList<AccessDecisionEvent>>(results);
    }
}

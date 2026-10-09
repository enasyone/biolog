using System.Collections.Concurrent;

namespace Biolog.Features.Transport.Domain;

internal sealed class InMemoryStopRepository : IStopRepository
{
    private readonly ConcurrentDictionary<string, StopRecord> _records = new(StringComparer.Ordinal);

    public Task<StopRecord> CreateAsync(StopRecord record, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (record.Version != 1 || record.Status != StopStatus.Active)
        {
            throw new TransportError($"a newly created stop must start Active at version 1: {record.Id}", "invalid_initial_state", 500);
        }

        if (!_records.TryAdd(record.Id, record))
        {
            throw new TransportError($"stop id collision: {record.Id}", "stop_id_collision", 500, new Dictionary<string, object?> { ["id"] = record.Id });
        }

        return Task.FromResult(record);
    }

    public Task<StopRecord?> GetAsync(string tenantId, string stopId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_records.TryGetValue(stopId, out var record) || record.TenantId != tenantId)
        {
            return Task.FromResult<StopRecord?>(null);
        }

        return Task.FromResult<StopRecord?>(record);
    }

    public Task<IReadOnlyList<StopRecord>> ListByTenantAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<StopRecord> items = [.. _records.Values.Where(r => r.TenantId == tenantId).OrderBy(r => r.CreatedAt, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal)];
        return Task.FromResult(items);
    }

    public Task<StopRecord> ReplaceAsync(StopRecord expected, StopRecord updated, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_records.TryGetValue(expected.Id, out var current) || current.TenantId != expected.TenantId)
        {
            throw TransportError.StopNotFound(expected.Id);
        }

        if (updated.Id != expected.Id || updated.TenantId != expected.TenantId)
        {
            throw new TransportError($"a stop's id and tenant_id may never change once created: {expected.Id}", "identity_immutable", 500);
        }

        if (expected.Status != updated.Status && !TransportTransitions.IsAllowedStopTransition(expected.Status, updated.Status))
        {
            throw TransportError.InvalidStatusTransition(expected.Id, expected.Status.ToWireString(), updated.Status.ToWireString());
        }

        if (!_records.TryUpdate(expected.Id, updated, expected))
        {
            var latest = _records.TryGetValue(expected.Id, out var p) ? p : current;
            throw TransportError.ConcurrencyConflict(expected.Id, expected.Version, latest.Version);
        }

        return Task.FromResult(updated);
    }
}

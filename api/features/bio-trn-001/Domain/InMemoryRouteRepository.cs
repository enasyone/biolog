using System.Collections.Concurrent;

namespace Biolog.Features.Transport.Domain;

/// <summary>A single, process-lifetime, cross-tenant dictionary standing in for the per-tenant database (the same simplification every in-memory store in this codebase draws around itself).</summary>
internal sealed class InMemoryRouteRepository : IRouteRepository
{
    private readonly ConcurrentDictionary<string, RouteRecord> _records = new(StringComparer.Ordinal);

    public Task<RouteRecord> CreateAsync(RouteRecord record, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (record.Version != 1 || record.Status != RouteStatus.Draft)
        {
            throw new TransportError($"a newly created route must start Draft at version 1: {record.Id}", "invalid_initial_state", 500);
        }

        if (!_records.TryAdd(record.Id, record))
        {
            throw new TransportError($"route id collision: {record.Id}", "route_id_collision", 500, new Dictionary<string, object?> { ["id"] = record.Id });
        }

        return Task.FromResult(record);
    }

    public Task<RouteRecord?> GetAsync(string tenantId, string routeId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_records.TryGetValue(routeId, out var record) || record.TenantId != tenantId)
        {
            return Task.FromResult<RouteRecord?>(null);
        }

        return Task.FromResult<RouteRecord?>(record);
    }

    public Task<IReadOnlyList<RouteRecord>> ListByTenantAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<RouteRecord> items = [.. _records.Values.Where(r => r.TenantId == tenantId).OrderBy(r => r.CreatedAt, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal)];
        return Task.FromResult(items);
    }

    public Task<RouteRecord> ReplaceAsync(RouteRecord expected, RouteRecord updated, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_records.TryGetValue(expected.Id, out var current) || current.TenantId != expected.TenantId)
        {
            throw TransportError.RouteNotFound(expected.Id);
        }

        if (updated.Id != expected.Id || updated.TenantId != expected.TenantId)
        {
            throw new TransportError($"a route's id and tenant_id may never change once created: {expected.Id}", "identity_immutable", 500);
        }

        if (expected.Status != updated.Status && !TransportTransitions.IsAllowedRouteTransition(expected.Status, updated.Status))
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

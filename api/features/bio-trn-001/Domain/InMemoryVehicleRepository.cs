using System.Collections.Concurrent;

namespace Biolog.Features.Transport.Domain;

internal sealed class InMemoryVehicleRepository : IVehicleRepository
{
    private readonly ConcurrentDictionary<string, VehicleRecord> _records = new(StringComparer.Ordinal);

    public Task<VehicleRecord> CreateAsync(VehicleRecord record, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (record.Version != 1 || record.Status != VehicleStatus.Active)
        {
            throw new TransportError($"a newly created vehicle must start Active at version 1: {record.Id}", "invalid_initial_state", 500);
        }

        if (!_records.TryAdd(record.Id, record))
        {
            throw new TransportError($"vehicle id collision: {record.Id}", "vehicle_id_collision", 500, new Dictionary<string, object?> { ["id"] = record.Id });
        }

        return Task.FromResult(record);
    }

    public Task<VehicleRecord?> GetAsync(string tenantId, string vehicleId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_records.TryGetValue(vehicleId, out var record) || record.TenantId != tenantId)
        {
            return Task.FromResult<VehicleRecord?>(null);
        }

        return Task.FromResult<VehicleRecord?>(record);
    }

    public Task<IReadOnlyList<VehicleRecord>> ListByTenantAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<VehicleRecord> items = [.. _records.Values.Where(r => r.TenantId == tenantId).OrderBy(r => r.CreatedAt, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal)];
        return Task.FromResult(items);
    }

    public Task<VehicleRecord> ReplaceAsync(VehicleRecord expected, VehicleRecord updated, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_records.TryGetValue(expected.Id, out var current) || current.TenantId != expected.TenantId)
        {
            throw TransportError.VehicleNotFound(expected.Id);
        }

        if (updated.Id != expected.Id || updated.TenantId != expected.TenantId)
        {
            throw new TransportError($"a vehicle's id and tenant_id may never change once created: {expected.Id}", "identity_immutable", 500);
        }

        if (expected.Status != updated.Status && !TransportTransitions.IsAllowedVehicleTransition(expected.Status, updated.Status))
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

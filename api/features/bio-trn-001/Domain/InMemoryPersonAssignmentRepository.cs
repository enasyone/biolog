using System.Collections.Concurrent;

namespace Biolog.Features.Transport.Domain;

internal sealed class InMemoryPersonAssignmentRepository : IPersonAssignmentRepository
{
    private readonly ConcurrentDictionary<string, PersonAssignment> _records = new(StringComparer.Ordinal);

    public Task<PersonAssignment> CreateAsync(PersonAssignment record, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (record.Version != 1)
        {
            throw new TransportError($"a newly created assignment must start at version 1: {record.Id}", "invalid_initial_state", 500);
        }

        if (!_records.TryAdd(record.Id, record))
        {
            throw new TransportError($"assignment id collision: {record.Id}", "assignment_id_collision", 500, new Dictionary<string, object?> { ["id"] = record.Id });
        }

        return Task.FromResult(record);
    }

    public Task<PersonAssignment?> GetAsync(string tenantId, string assignmentId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_records.TryGetValue(assignmentId, out var record) || record.TenantId != tenantId)
        {
            return Task.FromResult<PersonAssignment?>(null);
        }

        return Task.FromResult<PersonAssignment?>(record);
    }

    public Task<IReadOnlyList<PersonAssignment>> ListByTenantAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<PersonAssignment> items = [.. _records.Values.Where(r => r.TenantId == tenantId).OrderBy(r => r.CreatedAt, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal)];
        return Task.FromResult(items);
    }

    public Task<IReadOnlyList<PersonAssignment>> ListByPersonAsync(string tenantId, string personId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<PersonAssignment> items = [.. _records.Values.Where(r => r.TenantId == tenantId && r.PersonId == personId)];
        return Task.FromResult(items);
    }

    public Task<PersonAssignment> ReplaceAsync(PersonAssignment expected, PersonAssignment updated, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_records.TryGetValue(expected.Id, out var current) || current.TenantId != expected.TenantId)
        {
            throw TransportError.AssignmentNotFound(expected.Id);
        }

        if (updated.Id != expected.Id || updated.TenantId != expected.TenantId || updated.PersonId != expected.PersonId
            || updated.RouteId != expected.RouteId || updated.StopId != expected.StopId || updated.ValidFrom != expected.ValidFrom)
        {
            throw new TransportError($"an assignment's identity/person/route/stop/valid_from may never change once created: {expected.Id}", "identity_immutable", 500);
        }

        if (!_records.TryUpdate(expected.Id, updated, expected))
        {
            var latest = _records.TryGetValue(expected.Id, out var p) ? p : current;
            throw TransportError.ConcurrencyConflict(expected.Id, expected.Version, latest.Version);
        }

        return Task.FromResult(updated);
    }
}

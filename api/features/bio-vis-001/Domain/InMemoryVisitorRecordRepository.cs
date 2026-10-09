using System.Collections.Concurrent;

namespace Biolog.Features.Visitors.Domain;

/// <summary>
/// A single, process-lifetime, cross-tenant dictionary standing in for the per-tenant database
/// (the same simplification every in-memory store in this codebase draws around itself). See
/// <see cref="IVisitorRecordRepository"/> for why this class and its interface are
/// <c>internal</c>.
/// </summary>
internal sealed class InMemoryVisitorRecordRepository : IVisitorRecordRepository
{
    private readonly ConcurrentDictionary<string, VisitorRecord> _records = new(StringComparer.Ordinal);

    public Task<VisitorRecord> CreateAsync(VisitorRecord record, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (record.Version != 1 || record.HostApprovalStatus != HostApprovalStatus.Pending || record.VisitStatus != VisitStatus.Preregistered)
        {
            throw new VisitorError(
                $"a newly created visitor record must start Pending/Preregistered at version 1: {record.Id}", "invalid_initial_state", 500);
        }

        if (!_records.TryAdd(record.Id, record))
        {
            // Unreachable in practice - ids are freshly minted UUIDv7s - but a collision on a
            // client-mintable identifier must fail loudly, not silently overwrite another
            // visitor's record.
            throw new VisitorError(
                $"visitor id collision: {record.Id}", "visitor_id_collision", 500,
                new Dictionary<string, object?> { ["id"] = record.Id });
        }

        return Task.FromResult(record);
    }

    public Task<VisitorRecord?> GetAsync(string tenantId, string visitorId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_records.TryGetValue(visitorId, out var record) || record.TenantId != tenantId)
        {
            // Cross-tenant lookup returns null (caller maps to 404), never a distinct "exists
            // but forbidden" signal - ADR-0001 S1.5.
            return Task.FromResult<VisitorRecord?>(null);
        }

        return Task.FromResult<VisitorRecord?>(record);
    }

    public Task<IReadOnlyList<VisitorRecord>> ListByTenantAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<VisitorRecord> items = [.. _records.Values.Where(r => r.TenantId == tenantId).OrderBy(r => r.CreatedAt, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal)];
        return Task.FromResult(items);
    }

    public Task<VisitorRecord> ReplaceAsync(VisitorRecord expected, VisitorRecord updated, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_records.TryGetValue(expected.Id, out var current) || current.TenantId != expected.TenantId)
        {
            throw VisitorError.NotFound(expected.Id);
        }

        if (updated.Id != expected.Id || updated.TenantId != expected.TenantId)
        {
            throw new VisitorError(
                $"a visitor record's id and tenant_id may never change once created: {expected.Id}", "identity_immutable", 500);
        }

        if (expected.HostApprovalStatus != updated.HostApprovalStatus && !VisitorTransitions.IsAllowedApprovalTransition(expected.HostApprovalStatus, updated.HostApprovalStatus))
        {
            throw VisitorError.ApprovalAlreadyDecided(expected.Id, expected.HostApprovalStatus);
        }

        if (expected.VisitStatus != updated.VisitStatus && !VisitorTransitions.IsAllowedVisitTransition(expected.VisitStatus, updated.VisitStatus))
        {
            throw VisitorError.CheckInBlocked(expected.Id, "invalid_visit_transition", $"cannot move from {expected.VisitStatus} to {updated.VisitStatus}");
        }

        // Structural (record) equality: succeeds only if nothing has changed since the caller
        // read `expected` - the CAS this feature's optimistic concurrency depends on.
        if (!_records.TryUpdate(expected.Id, updated, expected))
        {
            var latest = _records.TryGetValue(expected.Id, out var p) ? p : current;
            throw VisitorError.ConcurrencyConflict(expected.Id, expected.Version, latest.Version);
        }

        return Task.FromResult(updated);
    }
}

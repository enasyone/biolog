using System.Collections.Concurrent;

namespace Biolog.Features.AttendanceSessions.Domain;

/// <summary>Process-lifetime, volatile - documentation precision, not a durability claim, the same "documented, not overclaimed" discipline every sibling in-memory store in this codebase carries.</summary>
public sealed class InMemoryAttendanceAdjustmentRepository : IAttendanceAdjustmentRepository
{
    private readonly ConcurrentDictionary<string, AttendanceAdjustment> _byAdjustmentId = new();

    public Task<AttendanceAdjustment> CreateAsync(AttendanceAdjustment adjustment, CancellationToken cancellationToken = default)
    {
        _byAdjustmentId[adjustment.AdjustmentId] = adjustment;
        return Task.FromResult(adjustment);
    }

    public Task<IReadOnlyList<AttendanceAdjustment>> ListForPersonAsync(string tenantId, string sessionId, string personId, CancellationToken cancellationToken = default)
    {
        var results = _byAdjustmentId.Values
            .Where(a => a.TenantId == tenantId && a.SessionId == sessionId && a.PersonId == personId)
            .OrderBy(a => a.AdjustedAt, StringComparer.Ordinal)
            .ToList();
        return Task.FromResult<IReadOnlyList<AttendanceAdjustment>>(results);
    }

    public Task<IReadOnlyList<AttendanceAdjustment>> ListForSessionAsync(string tenantId, string sessionId, CancellationToken cancellationToken = default)
    {
        var results = _byAdjustmentId.Values
            .Where(a => a.TenantId == tenantId && a.SessionId == sessionId)
            .OrderBy(a => a.AdjustedAt, StringComparer.Ordinal)
            .ToList();
        return Task.FromResult<IReadOnlyList<AttendanceAdjustment>>(results);
    }
}

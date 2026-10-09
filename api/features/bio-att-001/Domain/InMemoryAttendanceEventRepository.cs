using System.Collections.Concurrent;

namespace Biolog.Features.Attendance.Domain;

/// <summary>Process-lifetime, volatile - documentation precision, not a durability claim, the same "documented, not overclaimed" discipline every sibling in-memory store in this codebase carries.</summary>
public sealed class InMemoryAttendanceEventRepository : IAttendanceEventRepository
{
    private readonly ConcurrentDictionary<string, AttendanceEvent> _byEventId = new();
    private readonly ConcurrentDictionary<(string TenantId, string IdempotencyKey), AttendanceEvent> _byIdempotencyKey = new();

    public Task<AttendanceEvent> CreateAsync(AttendanceEvent attendanceEvent, CancellationToken cancellationToken = default)
    {
        _byEventId[attendanceEvent.EventId] = attendanceEvent;
        _byIdempotencyKey[(attendanceEvent.TenantId, attendanceEvent.IdempotencyKey)] = attendanceEvent;
        return Task.FromResult(attendanceEvent);
    }

    public Task<AttendanceEvent?> GetByIdempotencyKeyAsync(string tenantId, string idempotencyKey, CancellationToken cancellationToken = default) =>
        Task.FromResult(_byIdempotencyKey.TryGetValue((tenantId, idempotencyKey), out var found) ? found : null);

    public Task<IReadOnlyList<AttendanceEvent>> ListForPersonOnDateAsync(string tenantId, string personId, string date, CancellationToken cancellationToken = default)
    {
        var results = _byEventId.Values
            .Where(e => e.TenantId == tenantId && e.PersonId == personId && e.OccurredAt.StartsWith(date, StringComparison.Ordinal))
            .OrderBy(e => e.OccurredAt, StringComparer.Ordinal)
            .ToList();
        return Task.FromResult<IReadOnlyList<AttendanceEvent>>(results);
    }

    public Task<IReadOnlyList<AttendanceEvent>> ListForTenantAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        var results = _byEventId.Values
            .Where(e => e.TenantId == tenantId)
            .OrderBy(e => e.OccurredAt, StringComparer.Ordinal)
            .ToList();
        return Task.FromResult<IReadOnlyList<AttendanceEvent>>(results);
    }
}

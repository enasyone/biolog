using System.Collections.Concurrent;

namespace Biolog.Features.Attendance.Audit;

/// <summary>Process-lifetime, volatile - documentation precision, not a durability claim.</summary>
public sealed class InMemoryAttendanceAuditSink : IAttendanceAuditSink
{
    private readonly ConcurrentDictionary<string, AttendanceAuditEvent> _byEventId = new();

    public Task WriteAsync(AttendanceAuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        _byEventId[auditEvent.EventId] = auditEvent;
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_byEventId.ContainsKey(eventId));

    /// <summary>Test/evidence seam only - not part of <see cref="IAttendanceAuditSink"/>.</summary>
    public IReadOnlyList<AttendanceAuditEvent> All() => [.. _byEventId.Values];
}

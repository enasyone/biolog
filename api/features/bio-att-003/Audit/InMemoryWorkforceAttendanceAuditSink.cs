using System.Collections.Concurrent;

namespace Biolog.Features.WorkforceAttendance.Audit;

/// <summary>Process-lifetime, volatile - documentation precision, not a durability claim.</summary>
public sealed class InMemoryWorkforceAttendanceAuditSink : IWorkforceAttendanceAuditSink
{
    private readonly ConcurrentDictionary<string, WorkforceAttendanceAuditEvent> _byEventId = new();

    public Task WriteAsync(WorkforceAttendanceAuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        _byEventId[auditEvent.EventId] = auditEvent;
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_byEventId.ContainsKey(eventId));

    /// <summary>Test/evidence seam only - not part of <see cref="IWorkforceAttendanceAuditSink"/>.</summary>
    public IReadOnlyList<WorkforceAttendanceAuditEvent> All() => [.. _byEventId.Values];
}

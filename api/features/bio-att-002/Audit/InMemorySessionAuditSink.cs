using System.Collections.Concurrent;

namespace Biolog.Features.AttendanceSessions.Audit;

/// <summary>Process-lifetime, volatile - documentation precision, not a durability claim.</summary>
public sealed class InMemorySessionAuditSink : ISessionAuditSink
{
    private readonly ConcurrentDictionary<string, SessionAuditEvent> _byEventId = new();

    public Task WriteAsync(SessionAuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        _byEventId[auditEvent.EventId] = auditEvent;
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_byEventId.ContainsKey(eventId));

    /// <summary>Test/evidence seam only - not part of <see cref="ISessionAuditSink"/>.</summary>
    public IReadOnlyList<SessionAuditEvent> All() => [.. _byEventId.Values];
}

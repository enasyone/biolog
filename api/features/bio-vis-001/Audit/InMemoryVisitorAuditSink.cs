using System.Collections.Concurrent;

namespace Biolog.Features.Visitors.Audit;

/// <summary>Process-lifetime, volatile - documentation precision, not a durability claim.</summary>
public sealed class InMemoryVisitorAuditSink : IVisitorAuditSink
{
    private readonly ConcurrentDictionary<string, VisitorAuditEvent> _byEventId = new();

    public Task WriteAsync(VisitorAuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        _byEventId[auditEvent.EventId] = auditEvent;
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_byEventId.ContainsKey(eventId));

    /// <summary>Test/evidence seam only - not part of <see cref="IVisitorAuditSink"/>.</summary>
    public IReadOnlyList<VisitorAuditEvent> All() => [.. _byEventId.Values];
}

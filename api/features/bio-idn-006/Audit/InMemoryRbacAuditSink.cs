using System.Collections.Concurrent;

namespace Biolog.Features.Rbac.Audit;

/// <summary>In-process, append-only audit store for BIO-IDN-006. Process-lifetime only, not durable across a restart. See <see cref="IRbacAuditSink"/>.</summary>
public sealed class InMemoryRbacAuditSink : IRbacAuditSink
{
    private readonly ConcurrentQueue<RbacAuditEvent> _events = new();

    public Task WriteAsync(RbacAuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        _events.Enqueue(auditEvent);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_events.Any(e => e.EventId == eventId));

    /// <summary>Read-only snapshot, oldest first. Exposed for tests and for a future audit-read endpoint.</summary>
    public IReadOnlyList<RbacAuditEvent> Events => [.. _events];
}

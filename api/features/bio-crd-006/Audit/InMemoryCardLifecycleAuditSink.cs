using System.Collections.Concurrent;

namespace Biolog.Features.CardLifecycle.Audit;

/// <summary>In-process, append-only audit store for BIO-CRD-006. Process-lifetime only, not durable across a restart. See <see cref="ICardLifecycleAuditSink"/>.</summary>
public sealed class InMemoryCardLifecycleAuditSink : ICardLifecycleAuditSink
{
    private readonly ConcurrentQueue<CardLifecycleAuditEvent> _events = new();

    public Task WriteAsync(CardLifecycleAuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        _events.Enqueue(auditEvent);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_events.Any(e => e.EventId == eventId));

    /// <summary>Read-only snapshot, oldest first. Exposed for tests and for a future audit-read endpoint.</summary>
    public IReadOnlyList<CardLifecycleAuditEvent> Events => [.. _events];
}

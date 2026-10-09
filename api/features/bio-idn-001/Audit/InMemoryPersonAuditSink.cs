using System.Collections.Concurrent;

namespace Biolog.Features.PersonRegistry.Audit;

/// <summary>In-process, append-only audit store for BIO-IDN-001. Process-lifetime only, not durable across a restart. See <see cref="IPersonAuditSink"/>.</summary>
public sealed class InMemoryPersonAuditSink : IPersonAuditSink
{
    private readonly ConcurrentQueue<PersonAuditEvent> _events = new();

    public Task WriteAsync(PersonAuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        _events.Enqueue(auditEvent);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_events.Any(e => e.EventId == eventId));

    /// <summary>Read-only snapshot, oldest first. Exposed for tests and for a future audit-read endpoint.</summary>
    public IReadOnlyList<PersonAuditEvent> Events => [.. _events];
}

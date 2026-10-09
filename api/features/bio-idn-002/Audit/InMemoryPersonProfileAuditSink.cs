using System.Collections.Concurrent;

namespace Biolog.Features.PersonProfileRegistry.Audit;

/// <summary>In-process, append-only audit store for BIO-IDN-002. Process-lifetime only, not durable across a restart. See <see cref="IPersonProfileAuditSink"/>.</summary>
public sealed class InMemoryPersonProfileAuditSink : IPersonProfileAuditSink
{
    private readonly ConcurrentQueue<PersonProfileAuditEvent> _events = new();

    public Task WriteAsync(PersonProfileAuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        _events.Enqueue(auditEvent);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_events.Any(e => e.EventId == eventId));

    /// <summary>Read-only snapshot, oldest first. Exposed for tests and for a future audit-read endpoint.</summary>
    public IReadOnlyList<PersonProfileAuditEvent> Events => [.. _events];
}

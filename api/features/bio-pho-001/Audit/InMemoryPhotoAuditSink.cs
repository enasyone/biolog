using System.Collections.Concurrent;

namespace Biolog.Features.Photography.Audit;

/// <summary>In-process, append-only audit store for BIO-PHO-001. Process-lifetime only, not durable across a restart. See <see cref="IPhotoAuditSink"/>.</summary>
public sealed class InMemoryPhotoAuditSink : IPhotoAuditSink
{
    private readonly ConcurrentQueue<PhotoAuditEvent> _events = new();

    public Task WriteAsync(PhotoAuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        _events.Enqueue(auditEvent);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_events.Any(e => e.EventId == eventId));

    /// <summary>Read-only snapshot, oldest first. Exposed for tests and for a future audit-read endpoint.</summary>
    public IReadOnlyList<PhotoAuditEvent> Events => [.. _events];
}

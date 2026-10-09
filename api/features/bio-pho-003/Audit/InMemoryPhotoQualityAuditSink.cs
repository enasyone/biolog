using System.Collections.Concurrent;

namespace Biolog.Features.PhotoQuality.Audit;

/// <summary>In-process, append-only audit store for BIO-PHO-003. Process-lifetime only, not durable across a restart. See <see cref="IPhotoQualityAuditSink"/>.</summary>
public sealed class InMemoryPhotoQualityAuditSink : IPhotoQualityAuditSink
{
    private readonly ConcurrentQueue<PhotoQualityAuditEvent> _events = new();

    public Task WriteAsync(PhotoQualityAuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        _events.Enqueue(auditEvent);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_events.Any(e => e.EventId == eventId));

    /// <summary>Read-only snapshot, oldest first. Exposed for tests and for a future audit-read endpoint.</summary>
    public IReadOnlyList<PhotoQualityAuditEvent> Events => [.. _events];
}

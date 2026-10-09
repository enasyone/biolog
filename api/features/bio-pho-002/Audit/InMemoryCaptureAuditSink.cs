using System.Collections.Concurrent;

namespace Biolog.Features.PhotoBooth.Audit;

/// <summary>In-process, append-only audit store for BIO-PHO-002. Process-lifetime only, not durable across a restart. See <see cref="ICaptureAuditSink"/>.</summary>
public sealed class InMemoryCaptureAuditSink : ICaptureAuditSink
{
    private readonly ConcurrentQueue<CaptureAuditEvent> _events = new();

    public Task WriteAsync(CaptureAuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        _events.Enqueue(auditEvent);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_events.Any(e => e.EventId == eventId));

    /// <summary>Read-only snapshot, oldest first. Exposed for tests and for a future audit-read endpoint.</summary>
    public IReadOnlyList<CaptureAuditEvent> Events => [.. _events];
}

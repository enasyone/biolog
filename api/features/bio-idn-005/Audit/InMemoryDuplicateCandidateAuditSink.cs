using System.Collections.Concurrent;

namespace Biolog.Features.DuplicateReview.Audit;

/// <summary>In-process, append-only audit store for BIO-IDN-005. Process-lifetime only, not durable across a restart. See <see cref="IDuplicateCandidateAuditSink"/>.</summary>
public sealed class InMemoryDuplicateCandidateAuditSink : IDuplicateCandidateAuditSink
{
    private readonly ConcurrentQueue<DuplicateCandidateAuditEvent> _events = new();

    public Task WriteAsync(DuplicateCandidateAuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        _events.Enqueue(auditEvent);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_events.Any(e => e.EventId == eventId));

    /// <summary>Read-only snapshot, oldest first. Exposed for tests and for a future audit-read endpoint.</summary>
    public IReadOnlyList<DuplicateCandidateAuditEvent> Events => [.. _events];
}

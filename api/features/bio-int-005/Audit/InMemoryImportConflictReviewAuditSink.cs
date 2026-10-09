using System.Collections.Concurrent;

namespace Biolog.Features.ImportConflictReview.Audit;

/// <summary>In-process, append-only audit store for BIO-INT-005. Process-lifetime only, not durable across a restart. See <see cref="IImportConflictReviewAuditSink"/>.</summary>
public sealed class InMemoryImportConflictReviewAuditSink : IImportConflictReviewAuditSink
{
    private readonly ConcurrentQueue<ImportConflictReviewAuditEvent> _events = new();

    public Task WriteAsync(ImportConflictReviewAuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        _events.Enqueue(auditEvent);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_events.Any(e => e.EventId == eventId));

    /// <summary>Read-only snapshot, oldest first. Exposed for tests and for a future audit-read endpoint.</summary>
    public IReadOnlyList<ImportConflictReviewAuditEvent> Events => [.. _events];
}

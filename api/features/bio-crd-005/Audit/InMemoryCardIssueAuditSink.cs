using System.Collections.Concurrent;

namespace Biolog.Features.CardIssuance.Audit;

/// <summary>In-process, append-only audit store for BIO-CRD-005. Process-lifetime only, not durable across a restart. See <see cref="ICardIssueAuditSink"/>.</summary>
public sealed class InMemoryCardIssueAuditSink : ICardIssueAuditSink
{
    private readonly ConcurrentQueue<CardIssueAuditEvent> _events = new();

    public Task WriteAsync(CardIssueAuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        _events.Enqueue(auditEvent);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_events.Any(e => e.EventId == eventId));

    /// <summary>Read-only snapshot, oldest first. Exposed for tests and for a future audit-read endpoint.</summary>
    public IReadOnlyList<CardIssueAuditEvent> Events => [.. _events];
}

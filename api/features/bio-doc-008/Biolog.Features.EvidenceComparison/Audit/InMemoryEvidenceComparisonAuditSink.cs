using System.Collections.Concurrent;

namespace Biolog.Features.EvidenceComparison.Audit;

/// <summary>In-process, append-only audit store for BIO-DOC-008 - the same seam every prior
/// audited feature's own <c>InMemoryXxxAuditSink</c> uses; a real durable sink is a later
/// story's job.</summary>
public sealed class InMemoryEvidenceComparisonAuditSink : IEvidenceComparisonAuditSink
{
    private readonly ConcurrentQueue<EvidenceComparisonAuditEvent> _events = new();

    public Task WriteAsync(EvidenceComparisonAuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        _events.Enqueue(auditEvent);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_events.Any(e => e.EventId == eventId));

    /// <summary>Read-only snapshot, oldest first. Exposed for tests and for a future audit-read endpoint.</summary>
    public IReadOnlyList<EvidenceComparisonAuditEvent> Events => [.. _events];
}

using System.Collections.Concurrent;

namespace Biolog.Features.AuthenticityAssessment.Audit;

/// <summary>In-process, append-only audit store for BIO-DOC-009 - the same seam every prior
/// audited feature's own <c>InMemoryXxxAuditSink</c> uses; a real durable sink is a later
/// story's job.</summary>
public sealed class InMemoryAuthenticityAssessmentAuditSink : IAuthenticityAssessmentAuditSink
{
    private readonly ConcurrentQueue<AuthenticityAssessmentAuditEvent> _events = new();

    public Task WriteAsync(AuthenticityAssessmentAuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        _events.Enqueue(auditEvent);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_events.Any(e => e.EventId == eventId));

    /// <summary>Read-only snapshot, oldest first. Exposed for tests and for a future audit-read endpoint.</summary>
    public IReadOnlyList<AuthenticityAssessmentAuditEvent> Events => [.. _events];
}

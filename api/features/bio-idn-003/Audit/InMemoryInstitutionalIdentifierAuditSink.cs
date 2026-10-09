using System.Collections.Concurrent;

namespace Biolog.Features.InstitutionalIdentifiers.Audit;

/// <summary>In-process, append-only audit store for BIO-IDN-003. Process-lifetime only, not durable across a restart. See <see cref="IInstitutionalIdentifierAuditSink"/>.</summary>
public sealed class InMemoryInstitutionalIdentifierAuditSink : IInstitutionalIdentifierAuditSink
{
    private readonly ConcurrentQueue<InstitutionalIdentifierAuditEvent> _events = new();

    public Task WriteAsync(InstitutionalIdentifierAuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        _events.Enqueue(auditEvent);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_events.Any(e => e.EventId == eventId));

    /// <summary>Read-only snapshot, oldest first. Exposed for tests and for a future audit-read endpoint.</summary>
    public IReadOnlyList<InstitutionalIdentifierAuditEvent> Events => [.. _events];
}

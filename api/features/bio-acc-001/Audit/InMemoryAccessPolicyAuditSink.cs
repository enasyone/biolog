using System.Collections.Concurrent;

namespace Biolog.Features.AccessPolicy.Audit;

/// <summary>In-process, append-only audit store for BIO-ACC-001. Process-lifetime only, not durable across a restart. See <see cref="IAccessPolicyAuditSink"/>.</summary>
public sealed class InMemoryAccessPolicyAuditSink : IAccessPolicyAuditSink
{
    private readonly ConcurrentQueue<AccessPolicyAuditEvent> _events = new();

    public Task WriteAsync(AccessPolicyAuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        _events.Enqueue(auditEvent);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_events.Any(e => e.EventId == eventId));

    /// <summary>Read-only snapshot, oldest first. Exposed for tests and for a future audit-read endpoint.</summary>
    public IReadOnlyList<AccessPolicyAuditEvent> Events => [.. _events];
}

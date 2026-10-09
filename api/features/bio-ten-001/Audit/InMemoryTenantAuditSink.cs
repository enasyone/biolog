using System.Collections.Concurrent;

namespace Biolog.Features.TenantRegistry.Audit;

/// <summary>In-process, append-only audit store for BIO-TEN-001. See <see cref="ITenantAuditSink"/>.</summary>
public sealed class InMemoryTenantAuditSink : ITenantAuditSink
{
    private readonly ConcurrentQueue<TenantAuditEvent> _events = new();

    public Task WriteAsync(TenantAuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        _events.Enqueue(auditEvent);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_events.Any(e => e.EventId == eventId));

    /// <summary>Read-only snapshot, oldest first. Exposed for tests and for a future audit-read endpoint.</summary>
    public IReadOnlyList<TenantAuditEvent> Events => [.. _events];
}

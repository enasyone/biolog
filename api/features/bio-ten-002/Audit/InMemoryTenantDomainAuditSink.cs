using System.Collections.Concurrent;

namespace Biolog.Features.TenantDomainRegistry.Audit;

/// <summary>In-process, append-only audit store for BIO-TEN-002. Process-lifetime only, not durable across a restart. See <see cref="ITenantDomainAuditSink"/>.</summary>
public sealed class InMemoryTenantDomainAuditSink : ITenantDomainAuditSink
{
    private readonly ConcurrentQueue<TenantDomainAuditEvent> _events = new();

    public Task WriteAsync(TenantDomainAuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        _events.Enqueue(auditEvent);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_events.Any(e => e.EventId == eventId));

    /// <summary>Read-only snapshot, oldest first. Exposed for tests and for a future audit-read endpoint.</summary>
    public IReadOnlyList<TenantDomainAuditEvent> Events => [.. _events];
}

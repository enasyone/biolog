using System.Collections.Concurrent;

namespace Biolog.Features.AuditInvestigation.Audit;

/// <summary>In-process, append-only audit store for BIO-RPT-003's own actions. Process-lifetime only, not durable across a restart. See <see cref="IAuditInvestigationAuditSink"/>.</summary>
public sealed class InMemoryAuditInvestigationAuditSink : IAuditInvestigationAuditSink
{
    private readonly ConcurrentQueue<AuditInvestigationAuditEvent> _events = new();

    public Task WriteAsync(AuditInvestigationAuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        _events.Enqueue(auditEvent);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_events.Any(e => e.EventId == eventId));

    /// <summary>Read-only snapshot, oldest first, tenant-filtered. Exposed for tests.</summary>
    public IReadOnlyList<AuditInvestigationAuditEvent> EventsForTenant(string tenantId) =>
        [.. _events.Where(e => e.TenantId == tenantId)];
}

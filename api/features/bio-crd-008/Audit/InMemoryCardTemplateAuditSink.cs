using System.Collections.Concurrent;

namespace Biolog.Features.CardTemplates.Audit;

/// <summary>In-process, append-only audit store for BIO-CRD-008. Process-lifetime only. See <see cref="ICardTemplateAuditSink"/>.</summary>
public sealed class InMemoryCardTemplateAuditSink : ICardTemplateAuditSink
{
    private readonly ConcurrentQueue<CardTemplateAuditEvent> _events = new();

    public Task WriteAsync(CardTemplateAuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        _events.Enqueue(auditEvent);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_events.Any(e => e.EventId == eventId));

    /// <summary>Read-only snapshot, oldest first, tenant-filtered. Exposed for tests.</summary>
    public IReadOnlyList<CardTemplateAuditEvent> EventsForTenant(string tenantId) =>
        [.. _events.Where(e => e.TenantId == tenantId)];
}

using System.Collections.Concurrent;

namespace Biolog.Features.DocumentTemplateEditor.Audit;

/// <summary>In-process, append-only audit store for BIO-DOC-011. Process-lifetime only. See <see cref="IDocumentTemplateDraftAuditSink"/>.</summary>
public sealed class InMemoryDocumentTemplateDraftAuditSink : IDocumentTemplateDraftAuditSink
{
    private readonly ConcurrentQueue<DocumentTemplateDraftAuditEvent> _events = new();

    public Task WriteAsync(DocumentTemplateDraftAuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        _events.Enqueue(auditEvent);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_events.Any(e => e.EventId == eventId));

    /// <summary>Read-only snapshot, oldest first, tenant-filtered. Exposed for tests.</summary>
    public IReadOnlyList<DocumentTemplateDraftAuditEvent> EventsForTenant(string tenantId) =>
        [.. _events.Where(e => e.TenantId == tenantId)];
}

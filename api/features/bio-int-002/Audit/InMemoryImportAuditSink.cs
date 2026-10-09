using System.Collections.Concurrent;

namespace Biolog.Features.Integrations.Audit;

/// <summary>Process-lifetime, volatile - documentation precision, not a durability claim.</summary>
public sealed class InMemoryImportAuditSink : IImportAuditSink
{
    private readonly ConcurrentDictionary<string, ImportAuditEvent> _byEventId = new();

    public Task WriteAsync(ImportAuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        _byEventId[auditEvent.EventId] = auditEvent;
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_byEventId.ContainsKey(eventId));

    /// <summary>Test/evidence seam only - not part of <see cref="IImportAuditSink"/>.</summary>
    public IReadOnlyList<ImportAuditEvent> All() => [.. _byEventId.Values];
}

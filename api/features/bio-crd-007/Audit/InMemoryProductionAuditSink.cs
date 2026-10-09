using System.Collections.Concurrent;

namespace Biolog.Features.CardProduction.Audit;

/// <summary>In-process, append-only audit store for BIO-CRD-007. Process-lifetime only. See <see cref="IProductionAuditSink"/>.</summary>
public sealed class InMemoryProductionAuditSink : IProductionAuditSink
{
    private readonly ConcurrentQueue<ProductionAuditEvent> _events = new();

    public Task WriteAsync(ProductionAuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        _events.Enqueue(auditEvent);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_events.Any(e => e.EventId == eventId));

    /// <summary>Read-only snapshot, oldest first. Exposed for tests and a future audit-read endpoint.</summary>
    public IReadOnlyList<ProductionAuditEvent> Events => [.. _events];
}

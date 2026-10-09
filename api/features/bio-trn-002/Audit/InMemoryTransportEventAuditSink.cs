using System.Collections.Concurrent;

namespace Biolog.Features.TransportEvents.Audit;

/// <summary>Process-lifetime, volatile - documentation precision, not a durability claim.</summary>
public sealed class InMemoryTransportEventAuditSink : ITransportEventAuditSink
{
    private readonly ConcurrentDictionary<string, TransportEventAuditEvent> _byEventId = new();

    public Task WriteAsync(TransportEventAuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        _byEventId[auditEvent.EventId] = auditEvent;
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_byEventId.ContainsKey(eventId));

    /// <summary>Test/evidence seam only - not part of <see cref="ITransportEventAuditSink"/>.</summary>
    public IReadOnlyList<TransportEventAuditEvent> All() => [.. _byEventId.Values];
}

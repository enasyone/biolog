using System.Collections.Concurrent;

namespace Biolog.Features.Transport.Audit;

/// <summary>Process-lifetime, volatile - documentation precision, not a durability claim.</summary>
public sealed class InMemoryTransportAuditSink : ITransportAuditSink
{
    private readonly ConcurrentDictionary<string, TransportAuditEvent> _byEventId = new();

    public Task WriteAsync(TransportAuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        _byEventId[auditEvent.EventId] = auditEvent;
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_byEventId.ContainsKey(eventId));

    /// <summary>Test/evidence seam only - not part of <see cref="ITransportAuditSink"/>.</summary>
    public IReadOnlyList<TransportAuditEvent> All() => [.. _byEventId.Values];
}

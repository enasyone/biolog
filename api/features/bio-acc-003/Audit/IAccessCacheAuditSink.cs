namespace Biolog.Features.AccessCache.Audit;

/// <summary>
/// Append-only store for <see cref="AccessCacheAuditEvent"/>s. Mandatory, not optional
/// (ADR-0002 §5 / ADR-0003 Decision 7): the service awaits this before treating an accepted
/// presentation-event intake as complete, and a failure here surfaces to the caller as
/// <c>audit_unavailable</c> rather than a silent, unaudited success.
///
/// <see cref="WriteAsync"/> throwing does not, on its own, prove the event was never
/// persisted - <see cref="ExistsAsync"/> resolves that ambiguity by checking ground truth
/// instead of guessing (ADR-0003 Decision 7, element 4).
/// </summary>
public interface IAccessCacheAuditSink
{
    Task WriteAsync(AccessCacheAuditEvent auditEvent, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default);
}

internal sealed class InMemoryAccessCacheAuditSink : IAccessCacheAuditSink
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, AccessCacheAuditEvent> _events = new(StringComparer.Ordinal);

    public Task WriteAsync(AccessCacheAuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        _events[auditEvent.EventId] = auditEvent;
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_events.ContainsKey(eventId));
}

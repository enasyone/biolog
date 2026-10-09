namespace Biolog.Features.OfflineEnrolment.Audit;

/// <summary>
/// Append-only store for <see cref="EnrolmentAuditEvent"/>s. Mandatory, not optional (ADR-0002
/// §5 / ADR-0003 Decision 7): the service awaits this before treating an accepted package
/// intake, capture-policy write or duplicate decision as complete, and a failure here surfaces
/// to the caller as <c>audit_unavailable</c> rather than a silent, unaudited success.
/// </summary>
public interface IEnrolmentAuditSink
{
    Task WriteAsync(EnrolmentAuditEvent auditEvent, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default);
}

internal sealed class InMemoryEnrolmentAuditSink : IEnrolmentAuditSink
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, EnrolmentAuditEvent> _events = new(StringComparer.Ordinal);

    public Task WriteAsync(EnrolmentAuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        _events[auditEvent.EventId] = auditEvent;
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_events.ContainsKey(eventId));
}

namespace Biolog.Features.MobileVerification.Audit;

/// <summary>
/// Append-only store for <see cref="VerificationAuditEvent"/>s. Mandatory, not optional
/// (ADR-0002 §5 / ADR-0003 Decision 7): the service awaits this before treating a verification
/// request or a device-role change as complete, and a failure here surfaces to the caller as
/// <c>audit_unavailable</c> rather than a silent, unaudited success.
/// </summary>
public interface IVerificationAuditSink
{
    Task WriteAsync(VerificationAuditEvent auditEvent, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default);
}

internal sealed class InMemoryVerificationAuditSink : IVerificationAuditSink
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, VerificationAuditEvent> _events = new(StringComparer.Ordinal);

    public Task WriteAsync(VerificationAuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        _events[auditEvent.EventId] = auditEvent;
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_events.ContainsKey(eventId));
}

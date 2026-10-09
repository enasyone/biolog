using System.Collections.Concurrent;

namespace Biolog.Features.FingerprintEnrolment.Audit;

/// <summary>Process-lifetime, volatile in-memory <see cref="IFingerprintEnrolmentAuditSink"/> -
/// explicitly NOT durable (same documentation-precision discipline as BIO-IDN-001's
/// <c>InMemoryPersonAuditSink</c>).</summary>
public sealed class InMemoryFingerprintEnrolmentAuditSink : IFingerprintEnrolmentAuditSink
{
    private readonly ConcurrentDictionary<string, FingerprintEnrolmentAuditEvent> _events = new(StringComparer.Ordinal);

    public Task WriteAsync(FingerprintEnrolmentAuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);
        _events[auditEvent.EventId] = auditEvent;
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_events.ContainsKey(eventId));

    /// <summary>Test-only accessor - not part of the interface, so a caller can never depend on
    /// implementation-specific enumeration.</summary>
    public IReadOnlyList<FingerprintEnrolmentAuditEvent> AllEvents => _events.Values.ToArray();
}

using System.Collections.Concurrent;

namespace Biolog.Features.BiometricRetention.Audit;

/// <summary>Process-lifetime, volatile in-memory <see cref="IBiometricRetentionAuditSink"/> -
/// explicitly NOT durable (same documentation-precision discipline as BIO-BIO-002's own
/// <c>InMemoryFingerprintEnrolmentAuditSink</c>). Its <see cref="WriteAsync"/> cannot append and
/// then throw (it stores and returns <see cref="Task.CompletedTask"/> unconditionally), so the
/// ambiguous-throw branch in <see cref="Service.BiometricDeletionService"/>/<see cref="Service.RetentionPolicyService"/>
/// is the right pattern to have in place for this tier, not exercised against a genuine
/// append-then-throw case - the same scope-of-confirmation ADR-0003 Decision 7 itself
/// documents for <c>InMemoryTenantAuditSink</c>.</summary>
public sealed class InMemoryBiometricRetentionAuditSink : IBiometricRetentionAuditSink
{
    private readonly ConcurrentDictionary<string, BiometricRetentionAuditEvent> _events = new(StringComparer.Ordinal);

    public Task WriteAsync(BiometricRetentionAuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);
        _events[auditEvent.EventId] = auditEvent;
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_events.ContainsKey(eventId));

    /// <summary>Test-only accessor - not part of the interface, so a caller can never depend on
    /// implementation-specific enumeration.</summary>
    public IReadOnlyList<BiometricRetentionAuditEvent> AllEvents => _events.Values.ToArray();
}

using System.Collections.Concurrent;

namespace Biolog.Features.AccessDecisions.Audit;

/// <summary>Process-lifetime, volatile - documentation precision, not a durability claim.</summary>
public sealed class InMemoryAccessDecisionAuditSink : IAccessDecisionAuditSink
{
    private readonly ConcurrentDictionary<string, AccessDecisionAuditEvent> _byEventId = new();

    public Task WriteAsync(AccessDecisionAuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        _byEventId[auditEvent.EventId] = auditEvent;
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_byEventId.ContainsKey(eventId));

    /// <summary>Test/evidence seam only - not part of <see cref="IAccessDecisionAuditSink"/>.</summary>
    public IReadOnlyList<AccessDecisionAuditEvent> All() => [.. _byEventId.Values];
}

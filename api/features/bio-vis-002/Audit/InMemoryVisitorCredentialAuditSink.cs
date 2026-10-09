using System.Collections.Concurrent;

namespace Biolog.Features.VisitorCredentials.Audit;

/// <summary>Process-lifetime, volatile - documentation precision, not a durability claim.</summary>
public sealed class InMemoryVisitorCredentialAuditSink : IVisitorCredentialAuditSink
{
    private readonly ConcurrentDictionary<string, VisitorCredentialAuditEvent> _byEventId = new();

    public Task WriteAsync(VisitorCredentialAuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        _byEventId[auditEvent.EventId] = auditEvent;
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_byEventId.ContainsKey(eventId));

    /// <summary>F08's own remediation - <see cref="ConcurrentDictionary{TKey,TValue}.GetOrAdd(TKey,TValue)"/> IS the atomic create-if-absent primitive: when two threads race on the same key, exactly one value wins and both calls observe that same winning value, with no additional lock needed.</summary>
    public Task<VisitorCredentialAuditEvent> CreateIfAbsentAsync(VisitorCredentialAuditEvent auditEvent, CancellationToken cancellationToken = default) =>
        Task.FromResult(_byEventId.GetOrAdd(auditEvent.EventId, auditEvent));

    /// <summary>Test/evidence seam only - not part of <see cref="IVisitorCredentialAuditSink"/>.</summary>
    public IReadOnlyList<VisitorCredentialAuditEvent> All() => [.. _byEventId.Values];
}

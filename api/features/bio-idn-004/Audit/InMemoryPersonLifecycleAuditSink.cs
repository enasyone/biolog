using System.Collections.Concurrent;

namespace Biolog.Features.PersonLifecycle.Audit;

/// <summary>In-process, append-only audit store for BIO-IDN-004. Process-lifetime only, not durable across a restart. See <see cref="IPersonLifecycleAuditSink"/>.</summary>
public sealed class InMemoryPersonLifecycleAuditSink : IPersonLifecycleAuditSink
{
    private readonly ConcurrentQueue<PersonLifecycleAuditEvent> _events = new();

    public Task WriteAsync(PersonLifecycleAuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        _events.Enqueue(auditEvent);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_events.Any(e => e.EventId == eventId));

    public Task<IReadOnlyList<PersonLifecycleAuditEvent>> ListByPersonAsync(string tenantId, string personId, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<PersonLifecycleAuditEvent> items =
        [
            .. _events.Where(e => e.TenantId == tenantId && e.Target.Id == personId).OrderBy(e => e.OccurredAt, StringComparer.Ordinal),
        ];
        return Task.FromResult(items);
    }

    /// <summary>Read-only snapshot, oldest first. Exposed for tests.</summary>
    public IReadOnlyList<PersonLifecycleAuditEvent> Events => [.. _events];
}

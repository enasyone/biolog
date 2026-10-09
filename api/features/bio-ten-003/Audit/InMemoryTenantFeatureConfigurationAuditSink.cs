using System.Collections.Concurrent;

namespace Biolog.Features.TenantFeatureConfig.Audit;

/// <summary>In-process, append-only audit store for BIO-TEN-003. See <see cref="ITenantFeatureConfigurationAuditSink"/>.</summary>
public sealed class InMemoryTenantFeatureConfigurationAuditSink : ITenantFeatureConfigurationAuditSink
{
    private readonly ConcurrentQueue<TenantFeatureConfigurationAuditEvent> _events = new();

    public Task WriteAsync(TenantFeatureConfigurationAuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        _events.Enqueue(auditEvent);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_events.Any(e => e.EventId == eventId));

    /// <summary>Read-only snapshot, oldest first. Exposed for tests and for a future audit-read endpoint.</summary>
    public IReadOnlyList<TenantFeatureConfigurationAuditEvent> Events => [.. _events];
}

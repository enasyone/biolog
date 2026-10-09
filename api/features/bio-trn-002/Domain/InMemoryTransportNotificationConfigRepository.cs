using System.Collections.Concurrent;

namespace Biolog.Features.TransportEvents.Domain;

public sealed class InMemoryTransportNotificationConfigRepository : ITransportNotificationConfigRepository
{
    private readonly ConcurrentDictionary<string, TransportNotificationConfig> _byTenantId = new();

    public Task<TransportNotificationConfig> GetAsync(string tenantId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_byTenantId.TryGetValue(tenantId, out var found) ? found : TransportNotificationConfig.Default(tenantId));

    public Task<bool> TrySaveAsync(TransportNotificationConfig config, int expectedVersion, CancellationToken cancellationToken = default)
    {
        var current = _byTenantId.TryGetValue(config.TenantId, out var existing) ? existing : TransportNotificationConfig.Default(config.TenantId);
        if (current.Version != expectedVersion)
        {
            return Task.FromResult(false);
        }

        _byTenantId[config.TenantId] = config;
        return Task.FromResult(true);
    }
}

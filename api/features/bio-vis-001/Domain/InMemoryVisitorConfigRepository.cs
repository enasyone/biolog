using System.Collections.Concurrent;

namespace Biolog.Features.Visitors.Domain;

public sealed class InMemoryVisitorConfigRepository : IVisitorConfigRepository
{
    private readonly ConcurrentDictionary<string, VisitorTenantConfig> _byTenantId = new();

    public Task<VisitorTenantConfig> GetAsync(string tenantId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_byTenantId.TryGetValue(tenantId, out var found) ? found : VisitorTenantConfig.Default(tenantId));

    public Task<bool> TrySaveAsync(VisitorTenantConfig config, int expectedVersion, CancellationToken cancellationToken = default)
    {
        var current = _byTenantId.TryGetValue(config.TenantId, out var existing) ? existing : VisitorTenantConfig.Default(config.TenantId);
        if (current.Version != expectedVersion)
        {
            return Task.FromResult(false);
        }

        _byTenantId[config.TenantId] = config;
        return Task.FromResult(true);
    }
}

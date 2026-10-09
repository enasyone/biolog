using System.Collections.Concurrent;

namespace Biolog.Features.TenantFeatureConfig.Domain;

/// <summary>In-process store for BIO-TEN-003. See <see cref="ITenantFeatureConfigurationStore"/> for why.</summary>
public sealed class InMemoryTenantFeatureConfigurationStore : ITenantFeatureConfigurationStore
{
    private readonly ConcurrentDictionary<string, TenantFeatureConfiguration> _configs = new(StringComparer.Ordinal);

    public Task<TenantFeatureConfiguration?> GetAsync(string tenantId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_configs.TryGetValue(tenantId, out var config) ? config : null);

    public Task<TenantFeatureConfiguration> CreateAsync(TenantFeatureConfiguration config, CancellationToken cancellationToken = default)
    {
        if (!_configs.TryAdd(config.TenantId, config))
        {
            throw new TenantFeatureConfigurationError(
                $"a feature configuration already exists for tenant: {config.TenantId}", "configuration_already_exists", 500,
                new Dictionary<string, object?> { ["tenant_id"] = config.TenantId });
        }

        return Task.FromResult(config);
    }

    public Task<TenantFeatureConfiguration> ReplaceAsync(TenantFeatureConfiguration expected, TenantFeatureConfiguration updated, CancellationToken cancellationToken = default)
    {
        if (!_configs.TryGetValue(expected.TenantId, out var current))
        {
            throw TenantFeatureConfigurationError.ConcurrencyConflict(expected.TenantId, expected.Version, 0);
        }

        // Structural (record) equality: succeeds only if nothing has changed since the
        // caller read `expected` - the CAS this feature's optimistic concurrency depends on.
        if (!_configs.TryUpdate(expected.TenantId, updated, expected))
        {
            var latest = _configs.TryGetValue(expected.TenantId, out var t) ? t : current;
            throw TenantFeatureConfigurationError.ConcurrencyConflict(expected.TenantId, expected.Version, latest.Version);
        }

        return Task.FromResult(updated);
    }
}

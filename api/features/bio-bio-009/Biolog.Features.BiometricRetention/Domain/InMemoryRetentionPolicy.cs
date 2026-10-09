using System.Collections.Concurrent;

namespace Biolog.Features.BiometricRetention.Domain;

/// <summary>BIO-BIO-009-AC01. Every (tenant, category) pair defaults to
/// <see cref="RetentionDefaults.For"/> until explicitly configured - the same "unconfigured means
/// the documented default, never a surprise" behaviour BIO-BIO-008's own policy uses.</summary>
internal sealed class InMemoryRetentionPolicy : IRetentionPolicy
{
    private readonly ConcurrentDictionary<(string TenantId, RetentionCategory Category), int> _configured = new();

    public Task<RetentionPolicySetting> GetAsync(string tenantId, RetentionCategory category, CancellationToken cancellationToken)
    {
        if (_configured.TryGetValue((tenantId, category), out var days))
        {
            return Task.FromResult(new RetentionPolicySetting { RetentionDays = days, IsExplicitlyConfigured = true });
        }

        return Task.FromResult(new RetentionPolicySetting
        {
            RetentionDays = RetentionDefaults.For(category),
            IsExplicitlyConfigured = false,
        });
    }

    public Task SetAsync(string tenantId, RetentionCategory category, int retentionDays, CancellationToken cancellationToken)
    {
        if (retentionDays <= 0)
        {
            throw BiometricRetentionException.InvalidRetentionDays(retentionDays);
        }

        _configured[(tenantId, category)] = retentionDays;
        return Task.CompletedTask;
    }
}

namespace Biolog.Features.Authentication;

/// <summary>
/// Tenant-scoped provider configuration storage (BIO-INT-001-AC01). Every method takes
/// <paramref name="tenantId"/> explicitly - this project has no ambient tenant concept of its
/// own; <c>BioInt001Module</c> resolves the real tenant and passes the string down, the same
/// shape BIO-RPT-001's <c>INotificationRuleRepository</c> already uses.
///
/// <see cref="GetAsync"/> returns <c>null</c> for a provider that does not exist AND for one
/// that exists in a different tenant (ADR-0002 §4.3's "not found, never forbidden").
/// </summary>
public interface IAuthProviderConfigRepository
{
    Task<AuthProviderConfig> CreateAsync(string tenantId, AuthProviderRegistrationRequest request, CancellationToken cancellationToken);

    Task<AuthProviderConfig?> GetAsync(string tenantId, string providerId, CancellationToken cancellationToken);

    Task<IReadOnlyList<AuthProviderConfig>> ListAsync(string tenantId, CancellationToken cancellationToken);

    /// <summary>Every enabled provider configured for this tenant - the set
    /// <see cref="AuthenticationService"/>'s AC05 demo path (and any future real sign-in page)
    /// may offer a caller to authenticate against.</summary>
    Task<IReadOnlyList<AuthProviderConfig>> ListEnabledAsync(string tenantId, CancellationToken cancellationToken);

    /// <summary>Throws <see cref="AuthProviderConcurrencyConflictException"/> if
    /// <paramref name="request"/>'s <c>ExpectedVersion</c> does not match the stored version.
    /// Returns <c>null</c> for a provider the tenant cannot see, preserving the same
    /// not-found-not-forbidden rule as <see cref="GetAsync"/>.</summary>
    Task<AuthProviderConfig?> UpdateStatusAsync(
        string tenantId, string providerId, AuthProviderStatusChangeRequest request, CancellationToken cancellationToken);
}

public sealed class InMemoryAuthProviderConfigRepository : IAuthProviderConfigRepository
{
    private readonly Dictionary<string, AuthProviderConfig> _configsById = [];
    private readonly Lock _gate = new();

    public Task<AuthProviderConfig> CreateAsync(
        string tenantId, AuthProviderRegistrationRequest request, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var config = new AuthProviderConfig(
            ProviderId: Guid.NewGuid().ToString(),
            TenantId: tenantId,
            ProviderType: request.ProviderType,
            DisplayName: request.DisplayName,
            Enabled: true,
            Mfa: request.Mfa,
            ClaimMappingRules: request.ClaimMappingRules,
            Version: 1,
            CreatedAt: now,
            UpdatedAt: now);

        lock (_gate)
        {
            _configsById[config.ProviderId] = config;
        }

        return Task.FromResult(config);
    }

    public Task<AuthProviderConfig?> GetAsync(string tenantId, string providerId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(
                _configsById.TryGetValue(providerId, out var config) && config.TenantId == tenantId ? config : null);
        }
    }

    public Task<IReadOnlyList<AuthProviderConfig>> ListAsync(string tenantId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            IReadOnlyList<AuthProviderConfig> result = _configsById.Values.Where(c => c.TenantId == tenantId).ToList();
            return Task.FromResult(result);
        }
    }

    public Task<IReadOnlyList<AuthProviderConfig>> ListEnabledAsync(string tenantId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            IReadOnlyList<AuthProviderConfig> result =
                _configsById.Values.Where(c => c.TenantId == tenantId && c.Enabled).ToList();
            return Task.FromResult(result);
        }
    }

    public Task<AuthProviderConfig?> UpdateStatusAsync(
        string tenantId, string providerId, AuthProviderStatusChangeRequest request, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (!_configsById.TryGetValue(providerId, out var current) || current.TenantId != tenantId)
            {
                return Task.FromResult<AuthProviderConfig?>(null);
            }

            if (current.Version != request.ExpectedVersion)
            {
                throw new AuthProviderConcurrencyConflictException(current.Version);
            }

            var updated = current with { Enabled = request.Enabled, Version = current.Version + 1, UpdatedAt = DateTimeOffset.UtcNow };
            _configsById[providerId] = updated;
            return Task.FromResult<AuthProviderConfig?>(updated);
        }
    }
}

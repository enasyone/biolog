using System.Collections.Concurrent;

namespace Biolog.Features.FaceSearch.Domain;

/// <summary>
/// An in-process, in-memory <see cref="IFaceSearchFeatureFlagPolicy"/> for local development and
/// this story's own tests - explicitly NOT a real tenant-administration layer, the same scope
/// note every prior BIO-BIO synthetic policy documents. Every tenant defaults to DISABLED
/// (fail-closed, AC02) unless explicitly enabled via <see cref="EnableForTenant"/> - a
/// configuration-time operation (called by an operator/tenant-administration channel, never by
/// an ordinary search caller; <see cref="Service.FaceSearchService.SearchAsync"/> never calls it
/// itself). A later story implementing a real, persisted, RBAC-gated tenant feature-flag
/// administration surface implements this interface without
/// <see cref="Service.FaceSearchService"/> changing shape.
/// </summary>
public sealed class SyntheticFaceSearchFeatureFlagPolicy : IFaceSearchFeatureFlagPolicy
{
    private readonly ConcurrentDictionary<string, bool> _enabledByTenant = new(StringComparer.Ordinal);

    public bool IsEnabledForTenant(string tenantId) => _enabledByTenant.TryGetValue(tenantId, out var enabled) && enabled;

    public void EnableForTenant(string tenantId) => _enabledByTenant[tenantId] = true;

    public void DisableForTenant(string tenantId) => _enabledByTenant[tenantId] = false;
}

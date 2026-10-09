using System.Collections.Concurrent;

namespace Biolog.Features.Rbac.Domain;

/// <summary>A single, process-lifetime, cross-tenant dictionary keyed by <c>(TenantId, Role, PermissionKey)</c>. See <see cref="IRolePermissionGrantRepository"/>.</summary>
internal sealed class InMemoryRolePermissionGrantRepository : IRolePermissionGrantRepository
{
    private readonly ConcurrentDictionary<(string TenantId, RoleType Role, string PermissionKey), RolePermissionGrant> _grants = new();

    public Task<RolePermissionGrant?> GetAsync(string tenantId, RoleType role, string permissionKey, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _grants.TryGetValue((tenantId, role, permissionKey), out var grant);
        return Task.FromResult(grant);
    }

    public Task<IReadOnlyList<RolePermissionGrant>> ListGrantedForRoleAsync(string tenantId, RoleType role, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<RolePermissionGrant> items =
        [
            .. _grants.Values.Where(g => g.TenantId == tenantId && g.Role == role && g.Granted),
        ];
        return Task.FromResult(items);
    }

    public Task<RolePermissionGrant> UpsertAsync(RolePermissionGrant grant, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _grants[(grant.TenantId, grant.Role, grant.PermissionKey)] = grant;
        return Task.FromResult(grant);
    }
}

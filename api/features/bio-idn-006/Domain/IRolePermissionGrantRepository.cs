namespace Biolog.Features.Rbac.Domain;

/// <summary>
/// Storage for <see cref="RolePermissionGrant"/>, keyed by <c>(TenantId, Role, PermissionKey)</c>.
/// <c>internal</c> from the start. Unlike <see cref="IPersonRoleAssignmentRepository"/>,
/// <see cref="UpsertAsync"/> is an unconditional write, not a compare-and-set - the per-key lock
/// in <see cref="RolePermissionGrantLockRegistry"/> already fully serializes writers to that
/// exact key, so a client-supplied <c>expected_version</c> would be redundant defense with no
/// real race to defend against (the identical reasoning BIO-IDN-003's own
/// <c>IIdentifierUniquenessRuleRepository.UpsertAsync</c> header gives for its own admin
/// setting).
/// </summary>
internal interface IRolePermissionGrantRepository
{
    Task<RolePermissionGrant?> GetAsync(string tenantId, RoleType role, string permissionKey, CancellationToken cancellationToken = default);

    /// <summary>Every permission key currently GRANTED to this role in this tenant - the population PermissionService.HasPermissionAsync reads.</summary>
    Task<IReadOnlyList<RolePermissionGrant>> ListGrantedForRoleAsync(string tenantId, RoleType role, CancellationToken cancellationToken = default);

    Task<RolePermissionGrant> UpsertAsync(RolePermissionGrant grant, CancellationToken cancellationToken = default);
}

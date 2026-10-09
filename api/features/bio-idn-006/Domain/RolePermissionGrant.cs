namespace Biolog.Features.Rbac.Domain;

/// <summary>
/// BIO-IDN-006-AC02: "Permissions are tenant-scoped." WHICH permission keys a <see cref="RoleType"/>
/// carries is a per-tenant configuration, not a single hardcoded global mapping - natural key
/// <c>(TenantId, Role, PermissionKey)</c>, mirroring BIO-IDN-003's <c>IdentifierUniquenessRule</c>
/// shape exactly (a small, tenant-scoped admin setting no single caller "owns" a version of).
///
/// <see cref="PermissionKey"/> is a free-form, namespaced string (e.g. <c>"person.reactivate"</c>,
/// the exact shape BIO-IDN-004's own <c>PermissionResolver</c> already uses) - this story does
/// not own or enumerate every permission key the whole system will ever need, so it validates
/// FORMAT only (the same namespaced-verb pattern <c>audit.mjs</c>'s own action validation uses),
/// never a closed registry.
///
/// <b>Fail-closed default.</b> An absent grant means NOT granted - a role has no permissions
/// until a tenant explicitly grants specific ones. This is a stricter default than BIO-IDN-003's
/// own "missing rule defaults to Enforced=true" (that story owns and understands its own
/// invariant; this one is a cross-cutting facility with no built-in opinion about what any given
/// permission key should mean for any given role) - the safer failure direction for a new
/// security primitive with no real consumers yet is to grant nothing until asked.
///
/// <b><see cref="RoleType.SuperAdmin"/> never needs a grant record</b> - see that enum's header.
/// <see cref="PermissionService.HasPermissionAsync"/> short-circuits to <c>true</c> for
/// <c>SuperAdmin</c> before ever consulting this repository, and
/// <see cref="PermissionService.SetGrantAsync"/> rejects an attempt to write a grant record for
/// <c>SuperAdmin</c> at all (see <see cref="RbacError.SuperAdminGrantNotConfigurable"/>) - a
/// tenant can never accidentally (or adversarially) narrow Super Admin's authority via a
/// permission-grant misconfiguration.
///
/// <b>Construction is internal</b> (BIO-IDN-001-QA-01's lesson, applied from the start).
/// </summary>
public sealed record RolePermissionGrant
{
    public string TenantId { get; }
    public RoleType Role { get; }
    public string PermissionKey { get; }
    public required bool Granted { get; init; }
    public required int Version { get; init; }
    public required string CreatedAt { get; init; }
    public required string UpdatedAt { get; init; }

    internal RolePermissionGrant(string tenantId, RoleType role, string permissionKey)
    {
        if (!Identifiers.CanonicalId.IsTenantId(tenantId))
        {
            throw new ArgumentException($"'{tenantId}' is not a well-formed tenant id (ADR-0001)", nameof(tenantId));
        }

        if (string.IsNullOrWhiteSpace(permissionKey))
        {
            throw new ArgumentException("permissionKey must not be blank", nameof(permissionKey));
        }

        TenantId = tenantId;
        Role = role;
        PermissionKey = permissionKey;
    }
}

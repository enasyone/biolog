namespace Biolog.Features.Rbac.Domain;

/// <summary>
/// BIO-IDN-006-AC03: "Sensitive actions can require second approval." Whether a given
/// <see cref="ActionKey"/> requires maker-checker approval is a per-tenant toggle, natural key
/// <c>(TenantId, ActionKey)</c> - the same small, tenant-scoped admin-setting shape
/// <see cref="RolePermissionGrant"/> and BIO-IDN-003's <c>IdentifierUniquenessRule</c> both use.
///
/// <see cref="ActionKey"/> is a free-form, namespaced string (the same shape
/// <see cref="RolePermissionGrant.PermissionKey"/> uses) - this story does not own or enumerate
/// every sensitive action the whole system will ever define.
///
/// <b>Fail-OPEN default</b> (deliberately the opposite of <see cref="RolePermissionGrant"/>'s
/// fail-closed default). An absent rule means second approval is NOT required - this facility is
/// an explicit OPT-IN a tenant enables for a specific action key it has decided is sensitive;
/// nothing is silently gated behind an approval workflow nobody configured. Contrast with
/// <see cref="RolePermissionGrant"/>, whose fail-closed default protects against silently
/// granting authority; here the failure direction that matters is never silently BLOCKING an
/// action nobody asked to protect.
/// </summary>
public sealed record SensitiveActionRule
{
    public string TenantId { get; }
    public string ActionKey { get; }
    public required bool RequiresSecondApproval { get; init; }
    public required int Version { get; init; }
    public required string CreatedAt { get; init; }
    public required string UpdatedAt { get; init; }

    internal SensitiveActionRule(string tenantId, string actionKey)
    {
        if (!Identifiers.CanonicalId.IsTenantId(tenantId))
        {
            throw new ArgumentException($"'{tenantId}' is not a well-formed tenant id (ADR-0001)", nameof(tenantId));
        }

        if (string.IsNullOrWhiteSpace(actionKey))
        {
            throw new ArgumentException("actionKey must not be blank", nameof(actionKey));
        }

        TenantId = tenantId;
        ActionKey = actionKey;
    }
}

namespace Biolog.Features.TenantFeatureConfig.Domain;

/// <summary>
/// Round-2 remediation (ZB-TEN003-01/BIO-TEN-003-F01, Critical). This feature's own narrow seam
/// onto BIO-IDN-006's RBAC - implemented in <c>Biolog.Api.Modules.BioTen003Module</c> by
/// wrapping that story's own already-registered <c>PermissionService</c> directly, the same
/// cross-story gateway pattern every sibling story uses (e.g. BIO-INT-002's own
/// <c>IPermissionGateway</c>).
/// </summary>
public interface IPermissionGateway
{
    Task<bool> ActorHasPermissionAsync(string tenantId, string actorId, string permissionKey, CancellationToken cancellationToken = default);
}

namespace Biolog.Features.BiometricRetention.Domain;

/// <summary>This feature's RBAC integration seam - mirrors BIO-ACC-001's/BIO-ACC-003's/
/// BIO-MOB-005's own <c>IPermissionGateway</c> exactly. The real implementation (an adapter in
/// <c>Biolog.Api.Modules.BioBio009Module</c>) wraps BIO-IDN-006's already-registered
/// <c>PermissionService.ActorHasPermissionAsync</c> directly - a real grant lookup, never a bare
/// header compare (ADR-0006 Decision 2/3).</summary>
internal interface IPermissionGateway
{
    Task<bool> ActorHasPermissionAsync(string tenantId, string actorId, string permissionKey, CancellationToken cancellationToken = default);
}

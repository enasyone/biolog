namespace Biolog.Features.AccessCache.Domain;

/// <summary>This feature's RBAC integration seam - mirrors BIO-ACC-001's own <c>IPermissionGateway</c> exactly. The real implementation (an adapter in <c>Biolog.Api.Modules.BioAcc003Module</c>) wraps BIO-IDN-006's already-registered <c>PermissionService.ActorHasPermissionAsync</c> directly.</summary>
public interface IPermissionGateway
{
    Task<bool> ActorHasPermissionAsync(string tenantId, string actorId, string permissionKey, CancellationToken cancellationToken = default);
}

namespace Biolog.Features.OfflineEnrolment.Domain;

/// <summary>This feature's RBAC integration seam - mirrors BIO-ACC-001's/BIO-ACC-003's own <c>IPermissionGateway</c> exactly. The real implementation (an adapter in <c>Biolog.Api.Modules.BioMob005Module</c>) wraps BIO-IDN-006's already-registered <c>PermissionService.ActorHasPermissionAsync</c> directly.</summary>
public interface IPermissionGateway
{
    Task<bool> ActorHasPermissionAsync(string tenantId, string actorId, string permissionKey, CancellationToken cancellationToken = default);
}

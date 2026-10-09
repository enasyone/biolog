namespace Biolog.Features.MobileVerification.Domain;

/// <summary>This feature's RBAC integration seam - mirrors BIO-ACC-001's/BIO-ACC-003's/BIO-MOB-005's own <c>IPermissionGateway</c> exactly. The real implementation (an adapter in <c>Biolog.Api.Modules.BioMob004Module</c>) wraps BIO-IDN-006's already-registered <c>PermissionService.ActorHasPermissionAsync</c> directly. Used only by the human-facing device-role assignment route (AC03) - never by the device-authenticated verification/history routes, which are gated by <see cref="VerificationDeviceRole"/> instead.</summary>
public interface IPermissionGateway
{
    Task<bool> ActorHasPermissionAsync(string tenantId, string actorId, string permissionKey, CancellationToken cancellationToken = default);
}

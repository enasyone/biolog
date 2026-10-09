namespace Biolog.Features.DeviceOperations;

/// <summary>
/// SY-DEV004-01 (Seyi, round 1, Critical): this story's RBAC integration seam, added after the
/// first review found every route unauthorized - "following BIO-DEV-001/002/003 is not
/// sufficient justification... their documented missing authorization is inherited debt, not
/// an authorization grant, and BIO-DEV-004 materially expands the surface from registry/
/// diagnostic operations to remote reboot/configuration/firmware control." <c>Biolog.Api</c>'s
/// <c>BioDev004Module</c> implements this by wrapping BIO-IDN-006's own already-registered
/// <c>PermissionService.ActorHasPermissionAsync</c> directly - the same seam BIO-TRN-003's/
/// BIO-INT-004's/BIO-ATT-003's own <c>IPermissionGateway</c> already established and this
/// project copies rather than invents independently (ADR-0006 Decision 3).
/// </summary>
internal interface IPermissionGateway
{
    Task<bool> ActorHasPermissionAsync(string tenantId, string actorId, string permissionKey, CancellationToken cancellationToken = default);
}

/// <summary>Thrown by <see cref="DeviceHealthPollingService"/>/<see cref="DeviceRemoteJobService"/>
/// when the resolved actor does not hold the permission a route requires. Maps to HTTP 403 in
/// <c>BioDev004Module</c>.</summary>
public sealed class PermissionRequiredException(string permissionKey) : Exception(
    $"this action requires the calling actor to hold the '{permissionKey}' permission")
{
    public string PermissionKey { get; } = permissionKey;
}

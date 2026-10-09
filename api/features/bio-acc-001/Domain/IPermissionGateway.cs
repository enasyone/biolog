namespace Biolog.Features.AccessPolicy.Domain;

/// <summary>
/// This story's RBAC integration seam (permanent role-prompt scope: "RBAC integration"):
/// authoring, publishing and evaluating an access policy are all authorization decisions gated
/// by whether the calling actor already holds the required permission - never an open surface.
/// <c>Biolog.Api</c>'s <c>BioAcc001Module</c> implements this by wrapping BIO-IDN-006's own
/// already-registered <c>PermissionService.ActorHasPermissionAsync</c> directly (that method's own
/// header: never throws for a malformed/nonexistent actor id, simply reports "not granted" - the
/// correct signal for an authorization gate).
/// </summary>
internal interface IPermissionGateway
{
    Task<bool> ActorHasPermissionAsync(string tenantId, string actorId, string permissionKey, CancellationToken cancellationToken = default);
}

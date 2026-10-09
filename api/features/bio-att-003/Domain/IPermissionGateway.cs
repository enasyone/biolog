namespace Biolog.Features.WorkforceAttendance.Domain;

/// <summary>
/// This story's own RBAC integration seam, wrapping BIO-IDN-006's own already-registered
/// <c>PermissionService.ActorHasPermissionAsync</c> directly (the same pattern BIO-ATT-001's own
/// identically-named interface already established). Used by every write this feature makes
/// (shift management, adjustment creation) - each is an authorization decision (ADR-0006
/// Decision 1/2), unlike reads (shift list, daily clock-pair/overtime summary), which stay open
/// to any asserted actor exactly like BIO-ATT-001's own capture/read split.
/// </summary>
public interface IPermissionGateway
{
    Task<bool> ActorHasPermissionAsync(string tenantId, string actorId, string permissionKey, CancellationToken cancellationToken = default);
}

namespace Biolog.Features.AttendanceSessions.Domain;

/// <summary>
/// This story's own RBAC integration seam, wrapping BIO-IDN-006's own already-registered
/// <c>PermissionService.ActorHasPermissionAsync</c> directly (the same pattern BIO-ATT-001
/// established). Used by <see cref="SessionAttendancePolicyService.SetAsync"/> and
/// <see cref="AttendanceAdjustmentService.CreateAdjustmentAsync"/> - the two authorization
/// decisions this feature makes (ADR-0006 Decision 1/2): setting a session-type's present/late/
/// absent thresholds changes how EVERY roster member's raw events get classified going forward,
/// and recording a manual adjustment overrides what a reader/gate/roster view shows for a specific
/// person - both are capabilities gated by identity, unlike session/roster CRUD or the read-side
/// classification view, which stay open to any asserted actor (audit-attribution only), the same
/// asymmetry BIO-ATT-001's own README re-derives for its own capture-vs-policy split.
/// </summary>
public interface IPermissionGateway
{
    Task<bool> ActorHasPermissionAsync(string tenantId, string actorId, string permissionKey, CancellationToken cancellationToken = default);
}

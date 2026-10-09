namespace Biolog.Features.Visitors.Domain;

/// <summary>
/// This story's own RBAC integration seam, wrapping BIO-IDN-006's own already-registered
/// <c>PermissionService.ActorHasPermissionAsync</c> directly (the same pattern BIO-ACC-001 and
/// BIO-ATT-001 established). Used by <see cref="VisitorRecordService"/> (<c>Approve</c>/<c>Deny</c>
/// - deciding who gets physical premises access) and <see cref="VisitorConfigService"/>
/// (<c>SetAsync</c> - a capability that changes every future visitor's requirements tenant-wide).
/// Preregistration, listing, reads, check-in/out and the evidence-capture actions
/// (NDA/safety-briefing/watchlist-result/temporary-credential) stay open to any asserted actor,
/// exactly like BIO-IDN-001 through BIO-IDN-005 and BIO-ATT-001's own capture/read - see
/// <c>docs/features/bio-vis-001/README.md</c> "Why preregistration/check-in/check-out stay open,
/// and approval/config do not" for the full ADR-0006 Decision 2 re-derivation this asymmetry
/// rests on (re-derived from this story's own data sensitivity, not pattern-matched from a
/// sibling story's endpoint shape - BIO-ACC-001's own QA-01 lesson).
/// </summary>
public interface IPermissionGateway
{
    Task<bool> ActorHasPermissionAsync(string tenantId, string actorId, string permissionKey, CancellationToken cancellationToken = default);
}

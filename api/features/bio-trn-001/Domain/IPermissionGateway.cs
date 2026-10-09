namespace Biolog.Features.Transport.Domain;

/// <summary>
/// This story's own RBAC integration seam, wrapping BIO-IDN-006's own already-registered
/// <c>PermissionService.ActorHasPermissionAsync</c> directly (the same pattern BIO-ACC-001,
/// BIO-ATT-001 and BIO-VIS-001 established). Used ONLY by <see cref="PersonAssignmentService"/>'s
/// <c>CreateAsync</c>/<c>EndAsync</c> - deciding a specific person may board a specific route is
/// granting physical transport access, this feature's one real authorization decision (the same
/// class as BIO-VIS-001's <c>visitor.approve</c> and BIO-ATT-001's policy-set).
///
/// Route/Vehicle/Stop create/update/retire and every read/list stay open to any asserted actor,
/// audit-attribution only - re-derived (not pattern-matched) from this story's own data
/// sensitivity: registry/inventory content here is comparable to BIO-ATT-001's own raw presence
/// events and PERSON's own name/DOB (which stay open across this codebase), not to BIO-ACC-001's
/// access-policy rule content (gated everywhere because IT is literally the blueprint for
/// bypassing physical security - a bus route name is not). See
/// <c>docs/features/bio-trn-001/README.md</c> "Why registry management stays open, and assignment
/// does not" for the full re-derivation (BIO-ACC-001's own QA-01 lesson: a sibling story's
/// precedent must be re-justified against what THIS story's own data actually discloses, never
/// pattern-matched on endpoint shape alone).
/// </summary>
public interface IPermissionGateway
{
    Task<bool> ActorHasPermissionAsync(string tenantId, string actorId, string permissionKey, CancellationToken cancellationToken = default);
}

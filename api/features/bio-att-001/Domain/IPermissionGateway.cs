namespace Biolog.Features.Attendance.Domain;

/// <summary>
/// This story's own RBAC integration seam, wrapping BIO-IDN-006's own already-registered
/// <c>PermissionService.ActorHasPermissionAsync</c> directly (the same pattern BIO-ACC-001
/// established). Used ONLY by <see cref="AttendancePolicyService"/> - setting the tenant-wide
/// interpretation policy is an authorization decision (it gates a capability by identity, ADR-0006
/// Decision 1/2), unlike event capture or reads, which stay open to any asserted actor exactly
/// like BIO-IDN-001 through BIO-IDN-005 (see <c>docs/features/bio-att-001/README.md</c>
/// "Why capture and read stay open, and policy does not" for the full ADR-0006 Decision 2
/// re-derivation this asymmetry rests on).
/// </summary>
public interface IPermissionGateway
{
    Task<bool> ActorHasPermissionAsync(string tenantId, string actorId, string permissionKey, CancellationToken cancellationToken = default);
}

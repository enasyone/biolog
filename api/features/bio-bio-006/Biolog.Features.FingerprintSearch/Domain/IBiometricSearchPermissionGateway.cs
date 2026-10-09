namespace Biolog.Features.FingerprintSearch.Domain;

/// <summary>
/// AC02: "Permissions distinguish 1:N from 1:1." This feature's own narrow seam onto BIO-IDN-006's
/// real RBAC store - deliberately NOT a <c>ProjectReference</c> to <c>api/features/bio-idn-006</c>
/// (this project's csproj remarks explain why: <c>PermissionService</c> is internal and
/// Scoped-registered). The real implementation is an adapter in
/// <c>Biolog.Api.Modules.BioBio006Module</c> wrapping BIO-IDN-006's own already-registered
/// <c>PermissionService.ActorHasPermissionAsync</c> - the same non-throwing, "unresolvable actor
/// means not authorized" internal counterpart <c>RoleAssignmentService</c>/<c>MakerCheckerService</c>
/// already use for their own authorization gates, the correct shape for THIS feature's gate too.
///
/// <b>AC02 in practice.</b> <see cref="Service.FingerprintSearchService.SearchPermissionKey"/> is
/// a permission key distinct from whatever key BIO-BIO-004's own 1:1 fingerprint verification
/// might one day require (BIO-BIO-004 itself performs no permission check today - a pre-existing,
/// accepted scope decision from that story, outside this story's write scope to retrofit - see
/// docs/features/bio-bio-006/README.md). "Distinguish 1:N from 1:1" is satisfied by this being a
/// GENUINELY SEPARATE key an operator must grant independently of any 1:1 capability, never a
/// key that also happens to authorize 1:1 verification or vice versa - a role holding one grant
/// does not thereby hold the other.
/// </summary>
public interface IBiometricSearchPermissionGateway
{
    Task<bool> HasPermissionAsync(string tenantId, string actorId, string permissionKey, CancellationToken cancellationToken = default);
}

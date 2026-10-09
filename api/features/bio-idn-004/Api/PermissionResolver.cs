using Microsoft.AspNetCore.Http;

namespace Biolog.Features.PersonLifecycle.Api;

/// <summary>
/// BIO-IDN-004-AC03: "Reactivation requires permission and audit reason." No RBAC story has
/// landed yet (BIO-IDN-006) - a real permission GRANT cannot be verified against anything real
/// today. This is the same honest, disclosed, asserted-not-verified accommodation
/// <see cref="ActorResolver"/> already uses for actor identity, applied to permission: the
/// caller must explicitly assert the specific permission string this operation requires via
/// <c>X-Actor-Permission</c>. This class only READS the header - <c>PersonLifecycleService</c>
/// itself (not this HTTP-layer helper) decides whether the asserted value satisfies a given
/// transition, so the check stays testable without an <see cref="HttpRequest"/>. A request
/// missing it (or asserting a different permission) is rejected before reactivation proceeds -
/// real verification of the CLAIM (does this actor genuinely hold this permission) is deferred
/// to BIO-IDN-006, exactly as real verification of actor IDENTITY is deferred to BIO-INT-001 -
/// see <c>docs/features/bio-idn-004/README.md</c>.
/// </summary>
public static class PermissionResolver
{
    public const string PermissionHeader = "X-Actor-Permission";
    public const string ReactivatePersonPermission = "person.reactivate";

    public static string? Resolve(HttpRequest request)
    {
        var asserted = request.Headers[PermissionHeader].ToString();
        return string.IsNullOrEmpty(asserted) ? null : asserted;
    }
}

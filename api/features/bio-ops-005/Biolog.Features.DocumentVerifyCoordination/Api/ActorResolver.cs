using Microsoft.AspNetCore.Http;

namespace Biolog.Features.DocumentVerifyCoordination.Api;

/// <summary>
/// No authentication/authorization boundary is built for this route family in this story - the
/// same disclosed seam BIO-DOC-009/010's own <c>ActorResolver</c> already established and Zainab's
/// BIO-DOC-009-F01 review already accepted for exactly this <c>/api/v1/documents/*</c> route
/// family: these are kiosk-style evidence-capture steps with no real caller identity to assert
/// (BIO-UX-009 never collects one), every step operates only on the caller's own just-captured
/// session (no cross-caller lookup by guessable id), and there is no RBAC/trusted-actor-provenance
/// boundary anywhere in this codebase for a composition-tier story to build unilaterally. A caller
/// may still name itself via <c>X-Actor-Id</c> for audit attribution; absent, a named synthetic
/// actor is recorded instead, never left blank.
/// </summary>
public static class ActorResolver
{
    public const string ActorIdHeader = "X-Actor-Id";
    public const string SyntheticUiActorId = "system:document-verify-ui";

    public static string ResolveOrDefault(HttpRequest request)
    {
        var actorId = request.Headers[ActorIdHeader].ToString();
        return string.IsNullOrWhiteSpace(actorId) ? SyntheticUiActorId : actorId;
    }
}

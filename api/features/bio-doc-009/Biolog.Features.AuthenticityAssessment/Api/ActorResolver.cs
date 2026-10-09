using Microsoft.AspNetCore.Http;

namespace Biolog.Features.AuthenticityAssessment.Api;

/// <summary>
/// No authentication story has landed in <c>api/Biolog.Api</c> yet (BIO-INT-001 is W12) - the same
/// disclosed seam BIO-IDN-001/BIO-TEN-001/BIO-TEN-002 already use. A caller may name itself via
/// <c>X-Actor-Id</c>. This is attribution, not authentication or authorization - see
/// <see cref="AuthenticityEndpoints"/>'s own remarks (Zainab's BIO-DOC-009-F01 review) for why no
/// currently-mapped HTTP route trusts this header for a consequential decision.
///
/// <para>
/// <see cref="Resolve"/> (strict, throws when absent) is kept for <see cref="AuthenticityAssessmentService.ApplyOverride"/>-style
/// callers - not currently wired to any HTTP route (see <see cref="AuthenticityEndpoints"/>'s own
/// remarks), but the seam a later story that builds a real authorization boundary maps a route onto.
/// </para>
///
/// <para>
/// <see cref="ResolveOrDefault"/> (falls back to a named synthetic actor) is used for
/// <see cref="AuthenticityEndpoints.ComputeAsync"/> - the primary compute route this story's own
/// AC06 connects the already-approved, already-reviewed <c>/document-verify</c> UI to, and that
/// UI's own live adapter (<c>web/features/documents/live.ts</c>) genuinely sends a bare POST with
/// no headers at all, by design (BIO-UX-009 never collects a real caller identity - there is no
/// login/session story in this codebase yet). Requiring an actor there would make this endpoint
/// permanently unreachable from the one UI AC06 exists to connect it to; an explicit
/// <c>X-Actor-Id</c> is still honoured when a caller (this story's own integration tests, or a
/// later authenticated caller) supplies one. This route carries no meaningful unauthorized-access
/// risk regardless of which actor id is recorded - see <see cref="AuthenticityEndpoints"/>'s own
/// remarks.
/// </para>
/// </summary>
public static class ActorResolver
{
    public const string ActorIdHeader = "X-Actor-Id";
    public const string SyntheticUiActorId = "system:document-verify-ui";

    public static string Resolve(HttpRequest request)
    {
        var actorId = request.Headers[ActorIdHeader].ToString();
        if (string.IsNullOrWhiteSpace(actorId))
        {
            throw AuthenticityAssessmentError.ActorRequired();
        }

        return actorId;
    }

    public static string ResolveOrDefault(HttpRequest request)
    {
        var actorId = request.Headers[ActorIdHeader].ToString();
        return string.IsNullOrWhiteSpace(actorId) ? SyntheticUiActorId : actorId;
    }
}

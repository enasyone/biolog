using Biolog.Features.AuthenticityAssessment.Model;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Biolog.Features.AuthenticityAssessment.Api;

/// <summary>
/// <c>/api/v1/documents/authenticity</c> (ADR-0004 S1.1 versioning; <c>/api/v1/documents/*</c>
/// matches every sibling BIO-DOC-009 evidence step's own already-reviewed URL family, see
/// <c>web/features/documents/live.ts</c>'s <c>LIVE_ENDPOINTS</c>).
///
/// <para>
/// <b>BIO-DOC-009-AC06:</b> <see cref="ComputeAsync"/> is the exact route/verb
/// <c>web/features/documents/live.ts</c> already POSTs to when
/// <c>NEXT_PUBLIC_FEATURE_MODE_DOCUMENTVERIFYSERVICE_AUTHENTICITY=LIVE</c> - see
/// <c>web/next.config.mjs</c>'s rewrite forwarding <c>/api/v1/documents/*</c> to this host.
/// Deliberately does NOT require an asserted actor (<see cref="ActorResolver.ResolveOrDefault"/>) -
/// this specific route's one caller, by this story's own AC06 design, sends no headers at all. It
/// carries no meaningful unauthorized-access risk: every call self-selects one of three fixed,
/// already-public SYNTHETIC demo sessions deterministically (no caller-supplied session id, no
/// lookup of arbitrary data, no rewrite of any prior decision) - see
/// <c>Demo/SyntheticAuthenticityFixtures.cs</c>.
/// </para>
///
/// <para>
/// <b>Zainab's security review (BIO-DOC-009-F01, High) - GET-by-id and override deliberately NOT
/// mapped here:</b> an earlier version of this file also mapped
/// <c>GET /api/v1/documents/authenticity/{id}</c> and
/// <c>POST .../{id}/override</c>. Zainab's review correctly found both exposed with no real
/// authorization boundary: the GET returned a full assessment (including individual evidence/check
/// results) to any caller who knew or guessed a session id, purely a caller-supplied,
/// non-authenticated <c>X-Actor-Id</c> header, treated as if it were authentication, let ANY caller
/// durably rewrite a disposition and have the forged identity recorded in the audit trail. No
/// RBAC/trusted-actor-provenance/tenant-context boundary exists anywhere in this codebase yet
/// (BIO-INT-001 is W12) - the same disclosed seam <see cref="ActorResolver"/>'s own remarks already
/// named, but that disclosure does not make an exposed, consequential HTTP surface safe to ship.
/// Per her review's own remediation options ("enforce trusted actor provenance ... or keep the
/// endpoints inaccessible until that boundary genuinely exists"): neither route is required by
/// AC06 (the approved UI only ever calls the compute route above; BIO-UX-009's own page has its own
/// separate, generic, purely-simulated human-review UI unrelated to this specific override
/// endpoint) or by AC05's own wording (AC05 requires the override CAPABILITY to exist and be
/// evidenced - satisfied entirely at the domain/service layer, see
/// <see cref="AuthenticityAssessmentService.Get"/>/<see cref="AuthenticityAssessmentService.ApplyOverride"/>
/// and their own dedicated tests - AC05 never says that capability must be reachable over HTTP).
/// Building real RBAC/tenant-context binding ad hoc for this one story would itself be a
/// shared-authorization-contract change this story has no standing to make unilaterally. The
/// correct-sized fix is therefore the "keep inaccessible" option: the HTTP handlers were removed
/// entirely (not just unmapped) so there is no dead, secured-away HTTP surface to bit-rot or be
/// re-enabled by accident; the service methods stay as the seam a later story - one that actually
/// builds the authorization boundary - maps real endpoints onto, the same "seam kept, only the
/// endpoint exposure deferred" shape <c>DocumentModule</c> (BIO-DOC-001) already established for
/// <c>IDocumentSessionStore</c>.
/// </para>
/// </summary>
public static class AuthenticityEndpoints
{
    private const string BasePath = "/api/v1/documents/authenticity";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(BasePath, ComputeAsync);
    }

    private static Task ComputeAsync(HttpContext context, AuthenticityAssessmentService service) => Handle(context, correlationId =>
    {
        var actorId = ActorResolver.ResolveOrDefault(context.Request);
        var assessment = service.ComputeNextDemoAssessment(actorId, correlationId);
        return Task.FromResult(Results.Ok(AuthenticityAssessmentResponse.From(assessment)));
    });

    private static async Task Handle(HttpContext context, Func<string, Task<IResult>> action)
    {
        var correlationId = CorrelationIdHeader.Resolve(context.Request);
        context.Response.Headers[CorrelationIdHeader.HeaderName] = correlationId;

        try
        {
            var result = await action(correlationId);
            await result.ExecuteAsync(context);
        }
        catch (AuthenticityAssessmentError error)
        {
            await ProblemResponse.WriteAsync(context, error, correlationId);
        }
    }
}

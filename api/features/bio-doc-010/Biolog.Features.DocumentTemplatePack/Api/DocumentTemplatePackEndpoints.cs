using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Biolog.Features.DocumentTemplatePack.Api;

/// <summary>
/// <c>POST /api/v1/documents/identify</c> (ADR-0004 S1.1 versioning; <c>/api/v1/documents/*</c>
/// matches every sibling BIO-DOC-009 evidence step's own already-reviewed URL family).
///
/// <para>
/// <b>BIO-DOC-010-AC05:</b> this is the exact route/verb
/// <c>web/features/documents/live.ts</c>'s existing <c>LIVE_ENDPOINTS['document-identification']</c>
/// already POSTs to - that step has been wired to this path since BIO-UX-009, but until this story
/// no backend existed there, so it always failed closed to <c>offline</c> (by design, a correctly
/// unconnected LIVE mode, not a bug - see that file's own remarks). No frontend file changes were
/// needed to connect it: this endpoint's response is a strict superset of the
/// <c>{ state, message, confidence? }</c> shape that step's generic live handler already parses.
/// </para>
///
/// <para>
/// Deliberately only one route, POST, no GET-by-id (Zainab's BIO-DOC-009-F01 review's lesson
/// applied from the start rather than found the hard way again): every call cycles through three
/// fixed, already-public SYNTHETIC demo scenarios deterministically - no caller-supplied session
/// id, no lookup of arbitrary data. <see cref="Api.ActorResolver"/>'s own remarks cover why
/// <c>X-Actor-Id</c> here is audit-attribution only (ADR-0006 Decision 1/2), never an
/// authorization decision.
/// </para>
/// </summary>
public static class DocumentTemplatePackEndpoints
{
    private const string BasePath = "/api/v1/documents/identify";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(BasePath, IdentifyAsync);
    }

    private static Task IdentifyAsync(HttpContext context, DocumentTemplatePackService service) => Handle(context, correlationId =>
    {
        var actorId = ActorResolver.ResolveOrDefault(context.Request);
        var outcome = service.IdentifyNextDemo(actorId, correlationId);
        return Task.FromResult(Results.Ok(DocumentTemplateIdentifyResponse.From(outcome)));
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
        catch (DocumentTemplatePackError error)
        {
            await ProblemResponse.WriteAsync(context, error, correlationId);
        }
    }
}

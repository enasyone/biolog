using System.Text.Json;
using Biolog.Features.AccessDecisions.Demo;
using Biolog.Features.AccessDecisions.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Biolog.Features.AccessDecisions.Api;

/// <summary>
/// <c>/api/v1/access-decisions/*</c> (ADR-0004 S1.1 versioning) - tenant-plane, except
/// <c>/api/v1/access/primary-action</c> (AC05 - see <see cref="AccessDecisionDemoBridgeService"/>'s
/// own header; mapped under BIO-UX-010's own existing base path since it is the specific
/// live-adapter route this story connects, not a new one of this story's own).
///
/// <b>Why only <c>primary-action</c>, not <c>view-access-events</c>.</b> BIO-ACC-003 (AccessCache)
/// already owns and maps <c>POST /api/v1/access/view-access-events</c> - discovered as a genuine
/// route collision while wiring this story's own AC05 (both features happened to target the same
/// pre-existing scaffolded route). BIO-ACC-003's own presentation-event log is the natural owner
/// of "View access events"; this story's own natural fit is "Create policy," which ties directly
/// into the SAME policy aggregate this feature's decision pipeline evaluates against. See
/// docs/features/bio-acc-002/README.md "AC05 web wiring" for the full record.
///
/// The decision route requires an asserted actor AND a verified provenance proof (ADR-0006
/// Decision 3, <see cref="ActorProvenanceGate"/>) - see that class's own header for why. Read
/// routes require only an asserted actor (audit attribution).
/// </summary>
public static class AccessDecisionEndpoints
{
    private const string BasePath = "/api/v1/access-decisions";
    private const string BridgeBasePath = "/api/v1/access";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(BasePath);
        group.MapPost("/decisions", DecideAsync);
        group.MapGet("/decisions", ListDecisionsAsync);

        // AC05 - web/features/access/live.ts's own primaryAction port, previously genuinely
        // unwired (BIO-UX-010's own scaffolding). See this class's own header for why
        // view-access-events is deliberately NOT mapped here.
        var bridgeGroup = endpoints.MapGroup(BridgeBasePath);
        bridgeGroup.MapPost("/primary-action", PrimaryActionAsync);
    }

    private static Task DecideAsync(HttpContext context, AccessDecisionService service, ActorProvenanceGate provenanceGate) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        provenanceGate.Require(context.Request, actor.Id);
        var request = await ReadJsonAsync<DecideAccessRequest>(context);
        var decision = await service.DecideAsync(request, actor, correlationId, context.RequestAborted);
        return Results.Ok(AccessDecisionResponse.From(decision));
    });

    private static Task ListDecisionsAsync(HttpContext context, AccessDecisionService service) => Handle(context, async _ =>
    {
        ActorResolver.Resolve(context.Request);
        var personId = context.Request.Query["person_id"].ToString();

        var decisions = string.IsNullOrWhiteSpace(personId)
            ? await service.ListForTenantAsync(context.RequestAborted)
            : await service.ListForPersonAsync(personId, context.RequestAborted);

        return Results.Ok(new AccessDecisionListResponse { Data = [.. decisions.Select(AccessDecisionResponse.From)] });
    });

    private static Task PrimaryActionAsync(HttpContext context, AccessDecisionDemoBridgeService bridge) => Handle(context, async _ =>
    {
        var message = await bridge.CreatePolicyAndComputeDemoDecisionAsync(context.RequestAborted);
        return Results.Ok(new { message });
    });

    private static async Task<T> ReadJsonAsync<T>(HttpContext context)
    {
        try
        {
            var body = await JsonSerializer.DeserializeAsync<T>(context.Request.Body, AccessDecisionJsonOptions.Strict, context.RequestAborted);
            if (body is null)
            {
                throw AccessDecisionError.Validation([new ValidationErrorItem("", "required", "a JSON request body is required")]);
            }

            return body;
        }
        catch (JsonException ex)
        {
            throw AccessDecisionError.Validation([new ValidationErrorItem("", "invalid_format", $"request body is not valid JSON for this endpoint: {ex.Message}")]);
        }
    }

    private static async Task Handle(HttpContext context, Func<string, Task<IResult>> action)
    {
        var correlationId = CorrelationIdHeader.Resolve(context.Request);
        context.Response.Headers[CorrelationIdHeader.HeaderName] = correlationId;

        try
        {
            var result = await action(correlationId);
            await result.ExecuteAsync(context);
        }
        catch (AccessDecisionError error)
        {
            await ProblemResponse.WriteAsync(context, error, correlationId);
        }
    }
}

using System.Text.Json;
using Biolog.Features.AccessPolicy.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Biolog.Features.AccessPolicy.Api;

/// <summary>
/// <c>/api/v1/access-policies</c> (ADR-0004 §1.1 versioning) - tenant-plane. EVERY route,
/// including <see cref="GetAsync"/>/<see cref="ListAsync"/>, requires an asserted actor
/// (<see cref="ActorResolver"/>) and a verified provenance proof for that actor id
/// (<see cref="ActorProvenanceGate"/>, ADR-0006 Decision 3), checked immediately after resolution
/// and before any authorization decision uses the value (BIO-ACC-001-QA-01, Chioma's acceptance
/// audit: a policy's own CONTENT - named subjects, deny rules, schedules - is the sensitive
/// asset, not just its ability to change, so read is gated exactly like write - see
/// <see cref="AccessPolicyService"/>'s own header for the full finding).
/// </summary>
public static class AccessPolicyEndpoints
{
    private const string PoliciesBasePath = "/api/v1/access-policies";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var policies = endpoints.MapGroup(PoliciesBasePath);
        policies.MapPost("", CreateDraftAsync);
        policies.MapGet("", ListAsync);
        policies.MapGet("/{policyKey}", GetAsync);
        policies.MapPut("/{policyKey}", ReviseDraftAsync);
        policies.MapPost("/{policyKey}/publish", PublishAsync);
        policies.MapPost("/{policyKey}/evaluate", EvaluateAsync);
    }

    private static Task CreateDraftAsync(HttpContext context, AccessPolicyService service, ActorProvenanceGate provenanceGate) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        provenanceGate.Require(context.Request, actor.Id);
        var request = await ReadJsonAsync<CreateAccessPolicyRequest>(context);
        var policy = await service.CreateDraftAsync(request, actor, correlationId, context.RequestAborted);
        return Results.Created($"{PoliciesBasePath}/{policy.PolicyKey}", AccessPolicyResponse.From(policy));
    });

    private static Task ReviseDraftAsync(HttpContext context, AccessPolicyService service, ActorProvenanceGate provenanceGate, string policyKey) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        provenanceGate.Require(context.Request, actor.Id);
        var request = await ReadJsonAsync<ReviseDraftRequest>(context);
        var policy = await service.ReviseDraftAsync(policyKey, request, actor, correlationId, context.RequestAborted);
        return Results.Ok(AccessPolicyResponse.From(policy));
    });

    private static Task PublishAsync(HttpContext context, AccessPolicyService service, ActorProvenanceGate provenanceGate, string policyKey) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        provenanceGate.Require(context.Request, actor.Id);
        var request = await ReadJsonAsync<PublishAccessPolicyRequest>(context);
        var policy = await service.PublishAsync(policyKey, request, actor, correlationId, context.RequestAborted);
        return Results.Ok(AccessPolicyResponse.From(policy));
    });

    private static Task EvaluateAsync(HttpContext context, PolicyEvaluationService service, ActorProvenanceGate provenanceGate, string policyKey) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        provenanceGate.Require(context.Request, actor.Id);
        var request = await ReadJsonAsync<EvaluateAccessPolicyRequest>(context);
        var result = await service.EvaluateAsync(policyKey, request, actor, correlationId, context.RequestAborted);
        return Results.Ok(EvaluateAccessPolicyResponse.From(result));
    });

    private static Task GetAsync(HttpContext context, AccessPolicyService service, ActorProvenanceGate provenanceGate, string policyKey) => Handle(context, async _ =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        provenanceGate.Require(context.Request, actor.Id);
        var policy = await service.GetAsync(policyKey, actor, context.RequestAborted);
        return Results.Ok(AccessPolicyResponse.From(policy));
    });

    private static Task ListAsync(HttpContext context, AccessPolicyService service, ActorProvenanceGate provenanceGate) => Handle(context, async _ =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        provenanceGate.Require(context.Request, actor.Id);
        var policies = await service.ListAsync(actor, context.RequestAborted);
        return Results.Ok(new AccessPolicyListResponse { Data = [.. policies.Select(AccessPolicyResponse.From)] });
    });

    internal static async Task<T> ReadJsonAsync<T>(HttpContext context)
    {
        try
        {
            var body = await JsonSerializer.DeserializeAsync<T>(context.Request.Body, AccessPolicyJsonOptions.Strict, context.RequestAborted);
            if (body is null)
            {
                throw AccessPolicyError.Validation([new ValidationErrorItem("", "required", "a JSON request body is required")]);
            }

            return body;
        }
        catch (JsonException ex)
        {
            throw AccessPolicyError.Validation([new ValidationErrorItem("", "invalid_format", $"request body is not valid JSON for this endpoint: {ex.Message}")]);
        }
    }

    internal static async Task Handle(HttpContext context, Func<string, Task<IResult>> action)
    {
        var correlationId = CorrelationIdHeader.Resolve(context.Request);
        context.Response.Headers[CorrelationIdHeader.HeaderName] = correlationId;

        try
        {
            var result = await action(correlationId);
            await result.ExecuteAsync(context);
        }
        catch (AccessPolicyError error)
        {
            await ProblemResponse.WriteAsync(context, error, correlationId);
        }
    }
}

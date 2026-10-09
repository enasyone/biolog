using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Biolog.Features.Rbac.Api;

/// <summary>
/// <c>/api/v1/sensitive-actions/{actionKey}/rule</c> (AC03's tenant-scoped configuration) and
/// <c>/api/v1/sensitive-actions/{actionKey}/approval-requests/{targetRef}</c> (the maker-checker
/// workflow itself). Every mutating route requires an asserted actor and, since
/// BIO-IDN-006-QA-08, a verified provenance proof (<see cref="ActorProvenanceGate"/>, ADR-0006
/// Decision 3) - and for
/// <see cref="DecideAsync"/>, that actor must NOT be the request's own maker (AC03's core
/// security property - see <c>MakerCheckerService.DecideAsync</c>).
/// </summary>
public static class MakerCheckerEndpoints
{
    private const string RuleBasePath = "/api/v1/sensitive-actions/{actionKey}/rule";
    private const string RequestsBasePath = "/api/v1/sensitive-actions/{actionKey}/approval-requests/{targetRef}";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPut(RuleBasePath, SetRuleAsync);
        endpoints.MapGet(RuleBasePath, GetRuleAsync);

        endpoints.MapPost(RequestsBasePath, RequestApprovalAsync);
        endpoints.MapGet(RequestsBasePath, ListHistoryAsync);
        endpoints.MapPost($"{RequestsBasePath}/{{sequenceNumber}}/decide", DecideAsync);
    }

    private static Task SetRuleAsync(HttpContext context, MakerCheckerService service, ActorProvenanceGate provenanceGate, string actionKey) =>
        RoleAssignmentEndpoints.Handle(context, async correlationId =>
        {
            var actor = ActorResolver.Resolve(context.Request);
            provenanceGate.Require(context.Request, actor.Id);
            var request = await RoleAssignmentEndpoints.ReadJsonAsync<SetSensitiveActionRuleRequest>(context);
            var rule = await service.SetActionRuleAsync(actionKey, request, actor, correlationId, context.RequestAborted);
            return Results.Ok(SensitiveActionRuleResponse.From(actionKey, rule));
        });

    private static Task GetRuleAsync(HttpContext context, MakerCheckerService service, string actionKey) =>
        RoleAssignmentEndpoints.Handle(context, async _ =>
        {
            var rule = await service.GetActionRuleAsync(actionKey, context.RequestAborted);
            return Results.Ok(SensitiveActionRuleResponse.From(actionKey, rule));
        });

    private static Task RequestApprovalAsync(HttpContext context, MakerCheckerService service, ActorProvenanceGate provenanceGate, string actionKey, string targetRef) =>
        RoleAssignmentEndpoints.Handle(context, async correlationId =>
        {
            var actor = ActorResolver.Resolve(context.Request);
            provenanceGate.Require(context.Request, actor.Id);
            var body = await RoleAssignmentEndpoints.ReadJsonAsync<RequestApprovalRequest>(context);
            var request = body with { TargetRef = targetRef };
            var created = await service.RequestApprovalAsync(actionKey, request, actor, correlationId, context.RequestAborted);
            return Results.Created(
                $"/api/v1/sensitive-actions/{actionKey}/approval-requests/{targetRef}/{created.SequenceNumber}", ApprovalRequestResponse.From(created));
        });

    private static Task ListHistoryAsync(HttpContext context, MakerCheckerService service, string actionKey, string targetRef) =>
        RoleAssignmentEndpoints.Handle(context, async _ =>
        {
            var history = await service.ListHistoryAsync(actionKey, targetRef, context.RequestAborted);
            return Results.Ok(new ApprovalRequestListResponse { Data = [.. history.Select(ApprovalRequestResponse.From)] });
        });

    private static Task DecideAsync(HttpContext context, MakerCheckerService service, ActorProvenanceGate provenanceGate, string actionKey, string targetRef, int sequenceNumber) =>
        RoleAssignmentEndpoints.Handle(context, async correlationId =>
        {
            var actor = ActorResolver.Resolve(context.Request);
            provenanceGate.Require(context.Request, actor.Id);
            var request = await RoleAssignmentEndpoints.ReadJsonAsync<DecideApprovalRequest>(context);
            var decided = await service.DecideAsync(actionKey, targetRef, sequenceNumber, request, actor, correlationId, context.RequestAborted);
            return Results.Ok(ApprovalRequestResponse.From(decided));
        });
}

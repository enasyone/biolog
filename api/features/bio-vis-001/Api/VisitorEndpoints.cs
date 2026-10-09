using System.Text.Json;
using Biolog.Features.Visitors.Demo;
using Biolog.Features.Visitors.Domain;
using Biolog.Features.Visitors.Identifiers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Biolog.Features.Visitors.Api;

/// <summary>
/// <c>/api/v1/visitors/*</c> (ADR-0004 S1.1 versioning) - tenant-plane, except the two remaining
/// <c>primary-action</c>/<c>view-visit-history</c> bridge routes (AC05 - see
/// <see cref="VisitorDemoBridgeService"/>'s own header for why those stay tenant/actor-free by
/// design).
///
/// <b>BIO-VIS-002 takes over <c>POST /{visitorId}/check-out</c>.</b> That route used to be
/// mapped and answered here; it is now mapped by
/// <c>Biolog.Features.VisitorCredentials.Api.VisitorCredentialEndpoints</c>, whose own
/// orchestrating checkout calls through to this feature's own <see cref="VisitorRecordService.CheckOutAsync"/>
/// (unchanged, still the real domain logic) before revoking any temporary credential that story
/// introduces - a visitor's check-out must always revoke a credential, and this feature has no
/// knowledge of that later story's own aggregate. See
/// <c>docs/features/bio-vis-002/README.md</c> "Why this story edits BIO-VIS-001's own endpoint
/// mapping" for the full disclosed-handoff record. The <c>check-out-visitor</c> HEADLESS DEMO
/// BRIDGE route below is unaffected - the demo UI never grants a credential, so there is nothing
/// for that path to revoke (documented there, not fixed in two places).
///
/// Preregistration/read/check-in/evidence-capture require an asserted actor (audit attribution)
/// but no provenance proof and no permission check - ADR-0006 Decision 2.
/// <see cref="ApproveAsync"/>/<see cref="DenyAsync"/>/<see cref="SetConfigAsync"/> are the
/// authorization decisions this feature makes and each require both a permission grant and a
/// verified ADR-0006 Decision 3 provenance proof.
/// </summary>
public static class VisitorEndpoints
{
    private const string BasePath = "/api/v1/visitors";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(BasePath);
        group.MapPost("", PreregisterAsync);
        group.MapGet("", ListAsync);
        group.MapGet("/{visitorId}", GetAsync);
        group.MapPost("/{visitorId}/approve", ApproveAsync);
        group.MapPost("/{visitorId}/deny", DenyAsync);
        group.MapPost("/{visitorId}/check-in", CheckInAsync);
        // AC03 (BIO-VIS-002) - /{visitorId}/check-out is now mapped and answered by
        // Biolog.Features.VisitorCredentials.Api.VisitorCredentialEndpoints (see this class's
        // own header). Not left duplicated here.
        group.MapPost("/{visitorId}/nda-acceptance", RecordNdaAcceptanceAsync);
        group.MapPost("/{visitorId}/safety-briefing", RecordSafetyBriefingAsync);
        group.MapPost("/{visitorId}/watchlist-result", RecordWatchlistResultAsync);
        group.MapPost("/{visitorId}/temporary-credential", IssueTemporaryCredentialAsync);
        group.MapGet("/config", GetConfigAsync);
        group.MapPut("/config", SetConfigAsync);

        // AC05 - the three routes web/features/visitors/live.ts already calls.
        group.MapPost("/primary-action", PrimaryActionAsync);
        group.MapPost("/check-out-visitor", CheckOutVisitorBridgeAsync);
        group.MapPost("/view-visit-history", ViewVisitHistoryBridgeAsync);
    }

    private static Task PreregisterAsync(HttpContext context, VisitorRecordService service) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        var request = await ReadJsonAsync<PreregisterVisitorRequest>(context);
        var record = await service.PreregisterAsync(request, actor, correlationId, context.RequestAborted);
        return Results.Ok(VisitorRecordResponse.From(record, UtcInstant.Now()));
    });

    private static Task ListAsync(HttpContext context, VisitorRecordService service) => Handle(context, async _ =>
    {
        ActorResolver.Resolve(context.Request);
        var records = await service.ListAsync(context.RequestAborted);
        var now = UtcInstant.Now();
        return Results.Ok(new VisitorRecordListResponse { Data = [.. records.Select(r => VisitorRecordResponse.From(r, now))] });
    });

    private static Task GetAsync(HttpContext context, VisitorRecordService service, string visitorId) => Handle(context, async _ =>
    {
        ActorResolver.Resolve(context.Request);
        var record = await service.GetAsync(visitorId, context.RequestAborted);
        return Results.Ok(VisitorRecordResponse.From(record, UtcInstant.Now()));
    });

    private static Task ApproveAsync(HttpContext context, VisitorRecordService service, ActorProvenanceGate provenanceGate, string visitorId) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        provenanceGate.Require(context.Request, actor.Id);
        var request = await ReadJsonAsync<VersionedVisitorRequest>(context);
        var record = await service.ApproveAsync(visitorId, request, actor, correlationId, context.RequestAborted);
        return Results.Ok(VisitorRecordResponse.From(record, UtcInstant.Now()));
    });

    private static Task DenyAsync(HttpContext context, VisitorRecordService service, ActorProvenanceGate provenanceGate, string visitorId) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        provenanceGate.Require(context.Request, actor.Id);
        var request = await ReadJsonAsync<DenyVisitorRequest>(context);
        var record = await service.DenyAsync(visitorId, request, actor, correlationId, context.RequestAborted);
        return Results.Ok(VisitorRecordResponse.From(record, UtcInstant.Now()));
    });

    private static Task CheckInAsync(HttpContext context, VisitorRecordService service, string visitorId) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        var request = await ReadJsonAsync<VersionedVisitorRequest>(context);
        var record = await service.CheckInAsync(visitorId, request, actor, correlationId, context.RequestAborted);
        return Results.Ok(VisitorRecordResponse.From(record, UtcInstant.Now()));
    });

    private static Task RecordNdaAcceptanceAsync(HttpContext context, VisitorRecordService service, string visitorId) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        var request = await ReadJsonAsync<VersionedVisitorRequest>(context);
        var record = await service.RecordNdaAcceptanceAsync(visitorId, request, actor, correlationId, context.RequestAborted);
        return Results.Ok(VisitorRecordResponse.From(record, UtcInstant.Now()));
    });

    private static Task RecordSafetyBriefingAsync(HttpContext context, VisitorRecordService service, string visitorId) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        var request = await ReadJsonAsync<VersionedVisitorRequest>(context);
        var record = await service.RecordSafetyBriefingAsync(visitorId, request, actor, correlationId, context.RequestAborted);
        return Results.Ok(VisitorRecordResponse.From(record, UtcInstant.Now()));
    });

    private static Task RecordWatchlistResultAsync(HttpContext context, VisitorRecordService service, string visitorId) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        var request = await ReadJsonAsync<RecordWatchlistResultRequest>(context);
        var record = await service.RecordWatchlistResultAsync(visitorId, request, actor, correlationId, context.RequestAborted);
        return Results.Ok(VisitorRecordResponse.From(record, UtcInstant.Now()));
    });

    private static Task IssueTemporaryCredentialAsync(HttpContext context, VisitorRecordService service, string visitorId) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        var request = await ReadJsonAsync<VersionedVisitorRequest>(context);
        var record = await service.IssueTemporaryCredentialAsync(visitorId, request, actor, correlationId, context.RequestAborted);
        return Results.Ok(VisitorRecordResponse.From(record, UtcInstant.Now()));
    });

    private static Task GetConfigAsync(HttpContext context, VisitorConfigService service) => Handle(context, async _ =>
    {
        ActorResolver.Resolve(context.Request);
        var config = await service.GetAsync(context.RequestAborted);
        return Results.Ok(VisitorConfigResponse.From(config));
    });

    private static Task SetConfigAsync(HttpContext context, VisitorConfigService service, ActorProvenanceGate provenanceGate) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        provenanceGate.Require(context.Request, actor.Id);
        var request = await ReadJsonAsync<SetVisitorConfigRequest>(context);
        var config = await service.SetAsync(request, actor, correlationId, context.RequestAborted);
        return Results.Ok(VisitorConfigResponse.From(config));
    });

    private static Task PrimaryActionAsync(HttpContext context, VisitorDemoBridgeService bridge) => Handle(context, async _ =>
    {
        var message = await bridge.CheckInNextDemoVisitorAsync(context.RequestAborted);
        return Results.Ok(new { message });
    });

    private static Task CheckOutVisitorBridgeAsync(HttpContext context, VisitorDemoBridgeService bridge) => Handle(context, async _ =>
    {
        var message = await bridge.CheckOutNextDemoVisitorAsync(context.RequestAborted);
        return Results.Ok(new { message });
    });

    private static Task ViewVisitHistoryBridgeAsync(HttpContext context, VisitorDemoBridgeService bridge) => Handle(context, async _ =>
    {
        var message = await bridge.ViewDemoVisitHistoryAsync(context.RequestAborted);
        return Results.Ok(new { message });
    });

    private static async Task<T> ReadJsonAsync<T>(HttpContext context)
    {
        try
        {
            var body = await JsonSerializer.DeserializeAsync<T>(context.Request.Body, VisitorJsonOptions.Strict, context.RequestAborted);
            if (body is null)
            {
                throw VisitorError.Validation([new ValidationErrorItem("", "required", "a JSON request body is required")]);
            }

            return body;
        }
        catch (JsonException ex)
        {
            throw VisitorError.Validation([new ValidationErrorItem("", "invalid_format", $"request body is not valid JSON for this endpoint: {ex.Message}")]);
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
        catch (VisitorError error)
        {
            await ProblemResponse.WriteAsync(context, error, correlationId);
        }
    }
}

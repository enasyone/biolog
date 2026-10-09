using System.Text.Json;
using Biolog.Features.TransportEvents.Demo;
using Biolog.Features.TransportEvents.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Biolog.Features.TransportEvents.Api;

/// <summary>
/// <c>/api/v1/transport-events/*</c> (ADR-0004 S1.1 versioning) - tenant-plane, except
/// <c>/api/v1/transport/primary-action</c> (AC05 - see <see cref="TransportEventDemoBridgeService"/>'s
/// own header; this route is mapped under BIO-TRN-001's own base path since it is the specific
/// existing live adapter route this story connects, not a new one of this story's own).
///
/// Every endpoint here requires an asserted actor (audit attribution) but no provenance proof and
/// no permission check - ADR-0006 Decision 2, re-derived fresh for this story's own data (see
/// <see cref="TransportEventService"/>'s and <see cref="TransportNotificationConfig"/>'s own
/// headers). This feature makes no authorization decision.
/// </summary>
public static class TransportEventEndpoints
{
    private const string BasePath = "/api/v1/transport-events";
    private const string BridgeBasePath = "/api/v1/transport";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(BasePath);
        group.MapPost("/events", CaptureAsync);
        group.MapGet("/events", ListEventsAsync);
        group.MapGet("/presence-status", GetPresenceStatusAsync);
        group.MapGet("/notification-config", GetNotificationConfigAsync);
        group.MapPut("/notification-config", SetNotificationConfigAsync);
        group.MapGet("/triggered-notifications", ListTriggeredNotificationsAsync);

        // AC05 - takes over web/features/transport/live.ts's own "View boarding/alighting log"
        // primary-action route from BIO-TRN-001's own TransportEndpoints.cs (see
        // docs/features/bio-trn-002/README.md "Why this story edits BIO-TRN-001's own endpoint
        // mapping" for the full, disclosed record of that handoff).
        var bridgeGroup = endpoints.MapGroup(BridgeBasePath);
        bridgeGroup.MapPost("/primary-action", PrimaryActionAsync);
    }

    private static Task CaptureAsync(HttpContext context, TransportEventService service) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        var request = await ReadJsonAsync<CaptureTransportEventRequest>(context);
        var captured = await service.CaptureAsync(request, actor, correlationId, context.RequestAborted);
        return Results.Ok(TransportEventResponse.From(captured));
    });

    private static Task ListEventsAsync(HttpContext context, TransportEventService service) => Handle(context, async _ =>
    {
        ActorResolver.Resolve(context.Request);
        var personId = context.Request.Query["person_id"].ToString();
        var date = context.Request.Query["date"].ToString();

        if (string.IsNullOrWhiteSpace(personId) || string.IsNullOrWhiteSpace(date))
        {
            var events = await service.ListForTenantAsync(context.RequestAborted);
            return Results.Ok(new TransportEventListResponse { Data = [.. events.Select(TransportEventResponse.From)] });
        }

        var forPerson = await service.ListForPersonOnDateAsync(personId, date, context.RequestAborted);
        return Results.Ok(new TransportEventListResponse { Data = [.. forPerson.Select(TransportEventResponse.From)] });
    });

    private static Task GetPresenceStatusAsync(HttpContext context, TransportEventService service) => Handle(context, async _ =>
    {
        ActorResolver.Resolve(context.Request);
        var personId = context.Request.Query["person_id"].ToString();
        var date = context.Request.Query["date"].ToString();

        if (string.IsNullOrWhiteSpace(personId) || string.IsNullOrWhiteSpace(date))
        {
            throw TransportEventError.Validation([new ValidationErrorItem("person_id/date", "required", "both person_id and date query parameters are required")]);
        }

        var status = await service.GetPresenceStatusAsync(personId, date, context.RequestAborted);
        return Results.Ok(DerivedTransportPresenceResponse.From(status));
    });

    private static Task GetNotificationConfigAsync(HttpContext context, TransportNotificationConfigService service) => Handle(context, async _ =>
    {
        ActorResolver.Resolve(context.Request);
        var config = await service.GetAsync(context.RequestAborted);
        return Results.Ok(TransportNotificationConfigResponse.From(config));
    });

    private static Task SetNotificationConfigAsync(HttpContext context, TransportNotificationConfigService service) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        var request = await ReadJsonAsync<SetTransportNotificationConfigRequest>(context);
        var config = await service.SetAsync(request, actor, correlationId, context.RequestAborted);
        return Results.Ok(TransportNotificationConfigResponse.From(config));
    });

    private static Task ListTriggeredNotificationsAsync(HttpContext context, TransportEventService service) => Handle(context, async _ =>
    {
        ActorResolver.Resolve(context.Request);
        var notifications = await service.ListTriggeredNotificationsAsync(context.RequestAborted);
        return Results.Ok(new TriggeredNotificationListResponse { Data = [.. notifications.Select(TriggeredNotificationResponse.From)] });
    });

    private static Task PrimaryActionAsync(HttpContext context, TransportEventDemoBridgeService bridge) => Handle(context, async _ =>
    {
        var message = await bridge.ViewBoardingAlightingLogAsync(context.RequestAborted);
        return Results.Ok(new { message });
    });

    private static async Task<T> ReadJsonAsync<T>(HttpContext context)
    {
        try
        {
            var body = await JsonSerializer.DeserializeAsync<T>(context.Request.Body, TransportEventJsonOptions.Strict, context.RequestAborted);
            if (body is null)
            {
                throw TransportEventError.Validation([new ValidationErrorItem("", "required", "a JSON request body is required")]);
            }

            return body;
        }
        catch (JsonException ex)
        {
            throw TransportEventError.Validation([new ValidationErrorItem("", "invalid_format", $"request body is not valid JSON for this endpoint: {ex.Message}")]);
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
        catch (TransportEventError error)
        {
            await ProblemResponse.WriteAsync(context, error, correlationId);
        }
    }
}

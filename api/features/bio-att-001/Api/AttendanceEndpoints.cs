using System.Text.Json;
using Biolog.Features.Attendance.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Biolog.Features.Attendance.Api;

/// <summary>
/// <c>/api/v1/attendance/*</c> (ADR-0004 S1.1 versioning) - tenant-plane.
///
/// <b>BIO-ATT-002 takes over <c>/api/v1/attendance/primary-action</c> and
/// <c>/api/v1/attendance/flag-exception</c>.</b> Those two routes used to be answered here by this
/// feature's own <c>Demo/AttendanceDemoBridgeService.cs</c> (removed - see that story's own README
/// "Deliberately out of scope" section, which already named these two as standing in for richer
/// capture/policy UI a later story would build). "View attendance record"/"Flag exception" are now
/// far better served by BIO-ATT-002's own session/roster interpretation than by this feature's flat
/// single-person daily status, so this story's own <c>Api/SessionEndpoints.cs</c> maps and answers
/// both routes itself - see <c>docs/features/bio-att-002/README.md</c> "Why this story edits
/// BIO-ATT-001's own endpoint mapping" for the full disclosed-handoff record.
///
/// <b>Capture and read (<see cref="CaptureAsync"/>, <see cref="ListEventsAsync"/>,
/// <see cref="GetStatusAsync"/>, <see cref="GetPolicyAsync"/>) require an asserted actor
/// (audit attribution) but no provenance proof and no permission check - ADR-0006 Decision 2 (see
/// <see cref="AttendanceEventService"/>'s header for the full re-derivation).</b>
/// <see cref="SetPolicyAsync"/> is the one authorization decision this feature makes and requires
/// both a permission grant and a verified ADR-0006 Decision 3 provenance proof.
/// </summary>
public static class AttendanceEndpoints
{
    private const string BasePath = "/api/v1/attendance";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(BasePath);
        group.MapPost("/events", CaptureAsync);
        group.MapGet("/events", ListEventsAsync);
        group.MapGet("/status", GetStatusAsync);
        group.MapGet("/policy", GetPolicyAsync);
        group.MapPut("/policy", SetPolicyAsync);

        // AC05 - primary-action/flag-exception now belong to BIO-ATT-002 (see this class's own header).
    }

    private static Task CaptureAsync(HttpContext context, AttendanceEventService service) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        var request = await ReadJsonAsync<CaptureAttendanceEventRequest>(context);
        var captured = await service.CaptureAsync(request, actor, correlationId, context.RequestAborted);
        return Results.Ok(AttendanceEventResponse.From(captured));
    });

    private static Task ListEventsAsync(HttpContext context, AttendanceEventService service) => Handle(context, async _ =>
    {
        ActorResolver.Resolve(context.Request);
        var personId = context.Request.Query["person_id"].ToString();
        var date = context.Request.Query["date"].ToString();

        if (string.IsNullOrWhiteSpace(personId) || string.IsNullOrWhiteSpace(date))
        {
            var events = await service.ListForTenantAsync(context.RequestAborted);
            return Results.Ok(new AttendanceEventListResponse { Data = [.. events.Select(AttendanceEventResponse.From)] });
        }

        var forPerson = await service.ListForPersonOnDateAsync(personId, date, context.RequestAborted);
        return Results.Ok(new AttendanceEventListResponse { Data = [.. forPerson.Select(AttendanceEventResponse.From)] });
    });

    private static Task GetStatusAsync(HttpContext context, AttendanceStatusService service) => Handle(context, async _ =>
    {
        ActorResolver.Resolve(context.Request);
        var personId = context.Request.Query["person_id"].ToString();
        var date = context.Request.Query["date"].ToString();

        if (string.IsNullOrWhiteSpace(personId) || string.IsNullOrWhiteSpace(date))
        {
            throw AttendanceError.Validation([new ValidationErrorItem("person_id/date", "required", "both person_id and date query parameters are required")]);
        }

        var status = await service.GetDailyStatusAsync(personId, date, context.RequestAborted);
        return Results.Ok(DerivedAttendanceStatusResponse.From(status));
    });

    private static Task GetPolicyAsync(HttpContext context, AttendancePolicyService service) => Handle(context, async _ =>
    {
        ActorResolver.Resolve(context.Request);
        var policy = await service.GetAsync(context.RequestAborted);
        return Results.Ok(AttendancePolicyResponse.From(policy));
    });

    private static Task SetPolicyAsync(HttpContext context, AttendancePolicyService service, ActorProvenanceGate provenanceGate) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        provenanceGate.Require(context.Request, actor.Id);
        var request = await ReadJsonAsync<SetAttendancePolicyRequest>(context);
        var policy = await service.SetAsync(request, actor, correlationId, context.RequestAborted);
        return Results.Ok(AttendancePolicyResponse.From(policy));
    });

    private static async Task<T> ReadJsonAsync<T>(HttpContext context)
    {
        try
        {
            var body = await JsonSerializer.DeserializeAsync<T>(context.Request.Body, AttendanceJsonOptions.Strict, context.RequestAborted);
            if (body is null)
            {
                throw AttendanceError.Validation([new ValidationErrorItem("", "required", "a JSON request body is required")]);
            }

            return body;
        }
        catch (JsonException ex)
        {
            throw AttendanceError.Validation([new ValidationErrorItem("", "invalid_format", $"request body is not valid JSON for this endpoint: {ex.Message}")]);
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
        catch (AttendanceError error)
        {
            await ProblemResponse.WriteAsync(context, error, correlationId);
        }
    }
}

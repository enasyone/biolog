using System.Text.Json;
using Biolog.Features.WorkforceAttendance.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Biolog.Features.WorkforceAttendance.Api;

/// <summary>
/// AC05: the real HTTP surface behind <c>/attendance</c>'s workforce shift/overtime/exception
/// UI. Tenant-plane scoped (ambient <c>ICurrentTenantProvider</c>, never a route-supplied
/// tenant id - unlike BIO-TEN-003's own admin-console shape, this surface is a single tenant's
/// own operator console, the same ambient-tenant binding BIO-ATT-001's own endpoints use).
/// Versioned under <c>/api/v1</c> per ADR-0004 Decision 1.1.
///
/// Writes (shift create/update, adjustment create) require <see cref="ActorProvenanceGate"/> +
/// a tenant-scoped permission grant (ADR-0006 Decision 1/2 - these are authorization decisions).
/// Reads (shift get/list, daily summary) stay open to any asserted actor, audit-attribution-only,
/// the same split BIO-ATT-001's own capture/read-vs-policy endpoints use.
/// </summary>
public static class WorkforceAttendanceEndpoints
{
    private const string ShiftsBasePath = "/api/v1/workforce-attendance/shifts";
    private const string SummaryBasePath = "/api/v1/workforce-attendance/people/{personId}/daily-summary/{date}";
    private const string AdjustmentBasePath = "/api/v1/workforce-attendance/people/{personId}/adjustments/{date}";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(ShiftsBasePath, CreateShiftAsync);
        endpoints.MapGet(ShiftsBasePath, ListShiftsAsync);
        endpoints.MapGet($"{ShiftsBasePath}/{{shiftId}}", GetShiftAsync);
        endpoints.MapPatch($"{ShiftsBasePath}/{{shiftId}}", UpdateShiftAsync);
        endpoints.MapGet(SummaryBasePath, GetDailySummaryAsync);
        endpoints.MapPost(AdjustmentBasePath, CreateAdjustmentAsync);
    }

    private static Task CreateShiftAsync(
        HttpContext context, WorkforceShiftService service, ActorProvenanceGate provenanceGate) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        provenanceGate.Require(context.Request, actor.Id);

        var request = await ReadJsonAsync<CreateWorkforceShiftRequest>(context);
        var shift = await service.CreateAsync(request, actor, correlationId, context.RequestAborted);
        return Results.Json(WorkforceShiftResponse.From(shift), statusCode: 201);
    });

    private static Task ListShiftsAsync(HttpContext context, WorkforceShiftService service) => Handle(context, async _ =>
    {
        var shifts = await service.ListAsync(context.RequestAborted);
        return Results.Json(new { data = shifts.Select(WorkforceShiftResponse.From) });
    });

    private static Task GetShiftAsync(HttpContext context, WorkforceShiftService service, string shiftId) => Handle(context, async _ =>
    {
        var shift = await service.GetAsync(shiftId, context.RequestAborted) ?? throw WorkforceAttendanceError.ShiftNotFound(shiftId);
        return Results.Json(WorkforceShiftResponse.From(shift));
    });

    private static Task UpdateShiftAsync(
        HttpContext context, WorkforceShiftService service, ActorProvenanceGate provenanceGate, string shiftId) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        provenanceGate.Require(context.Request, actor.Id);

        var request = await ReadJsonAsync<UpdateWorkforceShiftRequest>(context);
        var shift = await service.UpdateAsync(shiftId, request, actor, correlationId, context.RequestAborted);
        return Results.Json(WorkforceShiftResponse.From(shift));
    });

    private static Task GetDailySummaryAsync(
        HttpContext context, WorkforceAttendanceService service, string personId, string date) => Handle(context, async _ =>
    {
        var shiftId = context.Request.Query["shift_id"].ToString();
        if (string.IsNullOrWhiteSpace(shiftId))
        {
            throw WorkforceAttendanceError.Validation([new ValidationErrorItem("shift_id", "required", "the ?shift_id= query parameter is required so the summary knows which shift to evaluate against")]);
        }

        var summary = await service.GetDailySummaryAsync(personId, date, shiftId, context.RequestAborted);
        return Results.Json(WorkforceDailySummaryResponse.From(summary));
    });

    private static Task CreateAdjustmentAsync(
        HttpContext context, WorkforceAttendanceService service, ActorProvenanceGate provenanceGate, string personId, string date) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        provenanceGate.Require(context.Request, actor.Id);

        var request = await ReadJsonAsync<CreateWorkforceAttendanceAdjustmentRequest>(context);
        var adjustment = await service.CreateAdjustmentAsync(personId, date, request, actor, correlationId, context.RequestAborted);
        return Results.Json(WorkforceAttendanceAdjustmentResponse.From(adjustment), statusCode: 201);
    });

    private static async Task<T> ReadJsonAsync<T>(HttpContext context)
    {
        try
        {
            var body = await JsonSerializer.DeserializeAsync<T>(context.Request.Body, cancellationToken: context.RequestAborted);
            if (body is null)
            {
                throw WorkforceAttendanceError.Validation([new ValidationErrorItem("", "required", "a JSON request body is required")]);
            }

            return body;
        }
        catch (JsonException ex)
        {
            throw WorkforceAttendanceError.Validation([new ValidationErrorItem("", "invalid_format", $"request body is not valid JSON for this endpoint: {ex.Message}")]);
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
        catch (WorkforceAttendanceError error)
        {
            await ProblemResponse.WriteAsync(context, error, correlationId);
        }
    }
}

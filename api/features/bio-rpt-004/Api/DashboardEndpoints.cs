using Biolog.Features.OperationalDashboard.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Biolog.Features.OperationalDashboard.Api;

/// <summary>
/// BIO-RPT-004's own single read-only route, entirely under its own dedicated prefix - never
/// <c>/api/v1/reporting/...</c> (BIO-RPT-001/002's own paths) or <c>/api/v1/audit-investigations/...</c>
/// (BIO-RPT-003's own paths). The frontend connects through a new, dedicated <c>ReportingService</c>
/// port key (<c>operationalHealth</c>), the same multi-port precedent BIO-RPT-002/003 established
/// for this module - no collision with any existing key.
/// </summary>
public static class DashboardEndpoints
{
    private const string BasePath = "/api/v1/dashboard";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(BasePath);
        group.MapGet("/operational-health", GetOperationalHealthAsync);
    }

    private static Task GetOperationalHealthAsync(HttpContext context, DashboardService service, ActorProvenanceGate provenanceGate) => Handle(context, async _ =>
    {
        var actorId = ActorIdHeader.Resolve(context.Request);
        provenanceGate.Require(context.Request, actorId); // BIO-RPT-004-F03: before the permission check, not merely an asserted id
        var snapshot = await service.GetSnapshotAsync(actorId, context.RequestAborted);
        return Results.Ok(OperationalDashboardSnapshotResponse.From(snapshot));
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
        catch (DashboardError error)
        {
            await ProblemResponse.WriteAsync(context, error, correlationId);
        }
    }
}

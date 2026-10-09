using System.Text.Json;
using Biolog.Features.Transport.Demo;
using Biolog.Features.Transport.Domain;
using Biolog.Features.Transport.Identifiers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Biolog.Features.Transport.Api;

/// <summary>
/// <c>/api/v1/transport/*</c> (ADR-0004 S1.1 versioning) - tenant-plane, except the
/// <c>assign-route</c> bridge route (AC05 - see <see cref="TransportDemoBridgeService"/>'s own
/// header for why it stays tenant/actor-free by design).
///
/// <b>BIO-TRN-002 takes over <c>/api/v1/transport/primary-action</c>.</b> This route used to be
/// mapped and handled here (a registry-summary placeholder, disclosed at the time as standing in
/// for a boarding/alighting event log that did not exist yet - see this story's own README
/// "Deliberately out of scope"). BIO-TRN-002 now provides that capability and maps this exact
/// route itself (<c>Biolog.Features.TransportEvents.Api.TransportEventEndpoints</c>) - removed
/// here rather than left as dead, unreachable code. <c>assign-route</c> is unaffected.
///
/// Route/Vehicle/Stop create/read/update/retire and assignment read/list require an asserted
/// actor (audit attribution) but no provenance proof and no permission check - ADR-0006 Decision
/// 2. <see cref="CreateAssignmentAsync"/>/<see cref="EndAssignmentAsync"/> are the one
/// authorization decision this feature makes and each require both a permission grant and a
/// verified ADR-0006 Decision 3 provenance proof.
/// </summary>
public static class TransportEndpoints
{
    private const string BasePath = "/api/v1/transport";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(BasePath);

        group.MapPost("/routes", CreateRouteAsync);
        group.MapGet("/routes", ListRoutesAsync);
        group.MapGet("/routes/{routeId}", GetRouteAsync);
        group.MapPut("/routes/{routeId}", UpdateRouteAsync);
        group.MapPost("/routes/{routeId}/activate", ActivateRouteAsync);
        group.MapPost("/routes/{routeId}/retire", RetireRouteAsync);

        group.MapPost("/vehicles", CreateVehicleAsync);
        group.MapGet("/vehicles", ListVehiclesAsync);
        group.MapGet("/vehicles/{vehicleId}", GetVehicleAsync);
        group.MapPut("/vehicles/{vehicleId}", UpdateVehicleAsync);
        group.MapPost("/vehicles/{vehicleId}/out-of-service", SetVehicleOutOfServiceAsync);
        group.MapPost("/vehicles/{vehicleId}/reactivate", ReactivateVehicleAsync);
        group.MapPost("/vehicles/{vehicleId}/retire", RetireVehicleAsync);

        group.MapPost("/stops", CreateStopAsync);
        group.MapGet("/stops", ListStopsAsync);
        group.MapGet("/stops/{stopId}", GetStopAsync);
        group.MapPut("/stops/{stopId}", UpdateStopAsync);
        group.MapPost("/stops/{stopId}/retire", RetireStopAsync);

        group.MapPost("/assignments", CreateAssignmentAsync);
        group.MapGet("/assignments", ListAssignmentsAsync);
        group.MapGet("/assignments/{assignmentId}", GetAssignmentAsync);
        group.MapPost("/assignments/{assignmentId}/end", EndAssignmentAsync);

        // AC05 - assign-route only; primary-action now belongs to BIO-TRN-002 (see this class's own header).
        group.MapPost("/assign-route", AssignRouteBridgeAsync);
    }

    // --- Route ---

    private static Task CreateRouteAsync(HttpContext context, RouteService service) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        var request = await ReadJsonAsync<CreateRouteRequest>(context);
        var route = await service.CreateAsync(request, actor, correlationId, context.RequestAborted);
        return Results.Ok(RouteResponse.From(route));
    });

    private static Task ListRoutesAsync(HttpContext context, RouteService service) => Handle(context, async _ =>
    {
        ActorResolver.Resolve(context.Request);
        var routes = await service.ListAsync(context.RequestAborted);
        return Results.Ok(new RouteListResponse { Data = [.. routes.Select(RouteResponse.From)] });
    });

    private static Task GetRouteAsync(HttpContext context, RouteService service, string routeId) => Handle(context, async _ =>
    {
        ActorResolver.Resolve(context.Request);
        var route = await service.GetAsync(routeId, context.RequestAborted);
        return Results.Ok(RouteResponse.From(route));
    });

    private static Task UpdateRouteAsync(HttpContext context, RouteService service, string routeId) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        var request = await ReadJsonAsync<UpdateRouteRequest>(context);
        var route = await service.UpdateDetailsAsync(routeId, request, actor, correlationId, context.RequestAborted);
        return Results.Ok(RouteResponse.From(route));
    });

    private static Task ActivateRouteAsync(HttpContext context, RouteService service, string routeId) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        var request = await ReadJsonAsync<VersionedTransportRequest>(context);
        var route = await service.ActivateAsync(routeId, request, actor, correlationId, context.RequestAborted);
        return Results.Ok(RouteResponse.From(route));
    });

    private static Task RetireRouteAsync(HttpContext context, RouteService service, string routeId) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        var request = await ReadJsonAsync<VersionedTransportRequest>(context);
        var route = await service.RetireAsync(routeId, request, actor, correlationId, context.RequestAborted);
        return Results.Ok(RouteResponse.From(route));
    });

    // --- Vehicle ---

    private static Task CreateVehicleAsync(HttpContext context, VehicleService service) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        var request = await ReadJsonAsync<CreateVehicleRequest>(context);
        var vehicle = await service.CreateAsync(request, actor, correlationId, context.RequestAborted);
        return Results.Ok(VehicleResponse.From(vehicle));
    });

    private static Task ListVehiclesAsync(HttpContext context, VehicleService service) => Handle(context, async _ =>
    {
        ActorResolver.Resolve(context.Request);
        var vehicles = await service.ListAsync(context.RequestAborted);
        return Results.Ok(new VehicleListResponse { Data = [.. vehicles.Select(VehicleResponse.From)] });
    });

    private static Task GetVehicleAsync(HttpContext context, VehicleService service, string vehicleId) => Handle(context, async _ =>
    {
        ActorResolver.Resolve(context.Request);
        var vehicle = await service.GetAsync(vehicleId, context.RequestAborted);
        return Results.Ok(VehicleResponse.From(vehicle));
    });

    private static Task UpdateVehicleAsync(HttpContext context, VehicleService service, string vehicleId) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        var request = await ReadJsonAsync<UpdateVehicleRequest>(context);
        var vehicle = await service.UpdateDetailsAsync(vehicleId, request, actor, correlationId, context.RequestAborted);
        return Results.Ok(VehicleResponse.From(vehicle));
    });

    private static Task SetVehicleOutOfServiceAsync(HttpContext context, VehicleService service, string vehicleId) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        var request = await ReadJsonAsync<VersionedTransportRequest>(context);
        var vehicle = await service.SetOutOfServiceAsync(vehicleId, request, actor, correlationId, context.RequestAborted);
        return Results.Ok(VehicleResponse.From(vehicle));
    });

    private static Task ReactivateVehicleAsync(HttpContext context, VehicleService service, string vehicleId) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        var request = await ReadJsonAsync<VersionedTransportRequest>(context);
        var vehicle = await service.ReactivateAsync(vehicleId, request, actor, correlationId, context.RequestAborted);
        return Results.Ok(VehicleResponse.From(vehicle));
    });

    private static Task RetireVehicleAsync(HttpContext context, VehicleService service, string vehicleId) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        var request = await ReadJsonAsync<VersionedTransportRequest>(context);
        var vehicle = await service.RetireAsync(vehicleId, request, actor, correlationId, context.RequestAborted);
        return Results.Ok(VehicleResponse.From(vehicle));
    });

    // --- Stop ---

    private static Task CreateStopAsync(HttpContext context, StopService service) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        var request = await ReadJsonAsync<CreateStopRequest>(context);
        var stop = await service.CreateAsync(request, actor, correlationId, context.RequestAborted);
        return Results.Ok(StopResponse.From(stop));
    });

    private static Task ListStopsAsync(HttpContext context, StopService service) => Handle(context, async _ =>
    {
        ActorResolver.Resolve(context.Request);
        var stops = await service.ListAsync(context.RequestAborted);
        return Results.Ok(new StopListResponse { Data = [.. stops.Select(StopResponse.From)] });
    });

    private static Task GetStopAsync(HttpContext context, StopService service, string stopId) => Handle(context, async _ =>
    {
        ActorResolver.Resolve(context.Request);
        var stop = await service.GetAsync(stopId, context.RequestAborted);
        return Results.Ok(StopResponse.From(stop));
    });

    private static Task UpdateStopAsync(HttpContext context, StopService service, string stopId) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        var request = await ReadJsonAsync<UpdateStopRequest>(context);
        var stop = await service.UpdateDetailsAsync(stopId, request, actor, correlationId, context.RequestAborted);
        return Results.Ok(StopResponse.From(stop));
    });

    private static Task RetireStopAsync(HttpContext context, StopService service, string stopId) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        var request = await ReadJsonAsync<VersionedTransportRequest>(context);
        var stop = await service.RetireAsync(stopId, request, actor, correlationId, context.RequestAborted);
        return Results.Ok(StopResponse.From(stop));
    });

    // --- PersonAssignment ---

    private static Task CreateAssignmentAsync(HttpContext context, PersonAssignmentService service, ActorProvenanceGate provenanceGate) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        provenanceGate.Require(context.Request, actor.Id);
        var request = await ReadJsonAsync<CreatePersonAssignmentRequest>(context);
        var assignment = await service.CreateAsync(request, actor, correlationId, context.RequestAborted);
        return Results.Ok(PersonAssignmentResponse.From(assignment, UtcInstant.Now()));
    });

    private static Task ListAssignmentsAsync(HttpContext context, PersonAssignmentService service) => Handle(context, async _ =>
    {
        ActorResolver.Resolve(context.Request);
        var assignments = await service.ListAsync(context.RequestAborted);
        var now = UtcInstant.Now();
        return Results.Ok(new PersonAssignmentListResponse { Data = [.. assignments.Select(a => PersonAssignmentResponse.From(a, now))] });
    });

    private static Task GetAssignmentAsync(HttpContext context, PersonAssignmentService service, string assignmentId) => Handle(context, async _ =>
    {
        ActorResolver.Resolve(context.Request);
        var assignment = await service.GetAsync(assignmentId, context.RequestAborted);
        return Results.Ok(PersonAssignmentResponse.From(assignment, UtcInstant.Now()));
    });

    private static Task EndAssignmentAsync(HttpContext context, PersonAssignmentService service, ActorProvenanceGate provenanceGate, string assignmentId) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        provenanceGate.Require(context.Request, actor.Id);
        var request = await ReadJsonAsync<EndPersonAssignmentRequest>(context);
        var assignment = await service.EndAsync(assignmentId, request, actor, correlationId, context.RequestAborted);
        return Results.Ok(PersonAssignmentResponse.From(assignment, UtcInstant.Now()));
    });

    // --- AC05 bridge ---

    private static Task AssignRouteBridgeAsync(HttpContext context, TransportDemoBridgeService bridge) => Handle(context, async _ =>
    {
        var message = await bridge.AssignNextDemoPersonAsync(context.RequestAborted);
        return Results.Ok(new { message });
    });

    private static async Task<T> ReadJsonAsync<T>(HttpContext context)
    {
        try
        {
            var body = await JsonSerializer.DeserializeAsync<T>(context.Request.Body, TransportJsonOptions.Strict, context.RequestAborted);
            if (body is null)
            {
                throw TransportError.Validation([new ValidationErrorItem("", "required", "a JSON request body is required")]);
            }

            return body;
        }
        catch (JsonException ex)
        {
            throw TransportError.Validation([new ValidationErrorItem("", "invalid_format", $"request body is not valid JSON for this endpoint: {ex.Message}")]);
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
        catch (TransportError error)
        {
            await ProblemResponse.WriteAsync(context, error, correlationId);
        }
    }
}

using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Biolog.Features.PersonLifecycle.Api;

/// <summary>
/// <c>/api/v1/persons/{personId}/lifecycle</c> (ADR-0004 §1.1 versioning) - tenant-plane,
/// nested under the owning person, matching every sibling identity-wave story's convention.
/// Every mutating route requires an asserted actor (<see cref="ActorResolver"/>); reactivation
/// additionally requires an asserted permission (<see cref="PermissionResolver"/>).
/// </summary>
public static class PersonLifecycleEndpoints
{
    private const string BasePath = "/api/v1/persons/{personId}/lifecycle";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(BasePath);
        group.MapGet("", GetAsync);
        group.MapPost("/transitions", TransitionAsync);
        group.MapGet("/events", ListEventsAsync);
        group.MapPost("/revocations/reconcile", ReconcileAsync);
    }

    private static Task GetAsync(HttpContext context, PersonLifecycleService service, string personId) => Handle(context, async _ =>
    {
        var snapshot = await service.GetSnapshotAsync(personId, context.RequestAborted) ?? throw Domain.PersonLifecycleError.PersonNotFound(personId);
        var revocationPending = await service.HasPendingRevocationsAsync(personId, context.RequestAborted);
        return Results.Ok(PersonLifecycleResponse.From(snapshot, revocationPending));
    });

    private static Task TransitionAsync(HttpContext context, PersonLifecycleService service, string personId) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        var assertedPermission = PermissionResolver.Resolve(context.Request);
        var request = await ReadJsonAsync<TransitionRequest>(context);
        var updated = await service.TransitionAsync(personId, request, assertedPermission, actor, correlationId, context.RequestAborted);
        var revocationPending = await service.HasPendingRevocationsAsync(personId, context.RequestAborted);
        return Results.Ok(PersonLifecycleResponse.From(updated, revocationPending));
    });

    private static Task ListEventsAsync(HttpContext context, PersonLifecycleService service, string personId) => Handle(context, async _ =>
    {
        var events = await service.ListEventsAsync(personId, context.RequestAborted);
        return Results.Ok(new LifecycleEventListResponse { Data = [.. events.Select(LifecycleEventResponse.From)] });
    });

    /// <summary>BIO-IDN-004-QA-01 (Seyi round 3): reachable regardless of the person's current status - closes every outstanding pending suspension revocation.</summary>
    private static Task ReconcileAsync(HttpContext context, PersonLifecycleService service, string personId) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        var snapshot = await service.ReconcilePendingRevocationsAsync(personId, actor, correlationId, context.RequestAborted);
        var revocationPending = await service.HasPendingRevocationsAsync(personId, context.RequestAborted);
        return Results.Ok(PersonLifecycleResponse.From(snapshot, revocationPending));
    });

    private static async Task<T> ReadJsonAsync<T>(HttpContext context)
    {
        try
        {
            var body = await JsonSerializer.DeserializeAsync<T>(context.Request.Body, PersonLifecycleJsonOptions.Strict, context.RequestAborted);
            if (body is null)
            {
                throw Domain.PersonLifecycleError.Validation([new ValidationErrorItem("", "required", "a JSON request body is required")]);
            }

            return body;
        }
        catch (JsonException ex)
        {
            throw Domain.PersonLifecycleError.Validation([new ValidationErrorItem("", "invalid_format", $"request body is not valid JSON for this endpoint: {ex.Message}")]);
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
        catch (Domain.PersonLifecycleError error)
        {
            await ProblemResponse.WriteAsync(context, error, correlationId);
        }
    }
}

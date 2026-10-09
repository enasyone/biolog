using System.Text.Json;
using Biolog.Features.PersonRegistry.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Biolog.Features.PersonRegistry.Api;

/// <summary>
/// <c>/api/v1/persons</c> (ADR-0004 §1.1 versioning) - tenant-plane, matching
/// <c>/api/v1/devices</c>'s (BIO-DEV-001) convention: no <c>/control-plane</c> prefix, since
/// PERSON is not control-plane work. Every route requires an asserted actor
/// (<see cref="ActorResolver"/>) - none of these are anonymous/public the way BIO-TEN-002's
/// <c>/resolve</c> diagnostic is.
///
/// None of these routes require <c>Idempotency-Key</c>: ADR-0004 §4.3 names card activation,
/// production completion, offline sync intake and webhook intake as the required set, and
/// none of this feature's endpoints are those.
/// </summary>
public static class PersonEndpoints
{
    private const string BasePath = "/api/v1/persons";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(BasePath);
        group.MapPost("", RegisterAsync);
        group.MapGet("", ListAsync);
        group.MapGet("/{personId}", GetAsync);
        group.MapPut("/{personId}/status", UpdateStatusAsync);
    }

    private static Task RegisterAsync(HttpContext context, PersonService service) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        var request = await ReadJsonAsync<RegisterPersonRequest>(context);
        var person = await service.RegisterAsync(request, actor, correlationId, context.RequestAborted);
        return Results.Created($"{BasePath}/{person.Id}", PersonResponse.From(person));
    });

    private static Task GetAsync(HttpContext context, PersonService service, string personId) => Handle(context, async _ =>
    {
        var person = await service.GetAsync(personId, context.RequestAborted) ?? throw PersonError.NotFound(personId);
        return Results.Ok(PersonResponse.From(person));
    });

    private static Task ListAsync(HttpContext context, PersonService service) => Handle(context, async _ =>
    {
        var people = await service.ListAsync(context.RequestAborted);
        return Results.Ok(new PersonListResponse { Data = [.. people.Select(PersonResponse.From)] });
    });

    private static Task UpdateStatusAsync(HttpContext context, PersonService service, string personId) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        var request = await ReadJsonAsync<UpdatePersonStatusRequest>(context);
        var person = await service.UpdateStatusAsync(personId, request, actor, correlationId, context.RequestAborted);
        return Results.Ok(PersonResponse.From(person));
    });

    private static async Task<T> ReadJsonAsync<T>(HttpContext context)
    {
        try
        {
            var body = await JsonSerializer.DeserializeAsync<T>(context.Request.Body, PersonJsonOptions.Strict, context.RequestAborted);
            if (body is null)
            {
                throw PersonError.Validation([new ValidationErrorItem("", "required", "a JSON request body is required")]);
            }

            return body;
        }
        catch (JsonException ex)
        {
            throw PersonError.Validation([new ValidationErrorItem("", "invalid_format", $"request body is not valid JSON for this endpoint: {ex.Message}")]);
        }
    }

    /// <summary>
    /// Resolves the correlation id (ADR-0004 §3.2), runs <paramref name="action"/>, always
    /// echoes the resolved id back in <c>X-Correlation-Id</c> (success or failure), and maps
    /// a <see cref="PersonError"/> to a problem+json response instead of letting it become an
    /// unhandled 500.
    /// </summary>
    private static async Task Handle(HttpContext context, Func<string, Task<IResult>> action)
    {
        var correlationId = CorrelationIdHeader.Resolve(context.Request);
        context.Response.Headers[CorrelationIdHeader.HeaderName] = correlationId;

        try
        {
            var result = await action(correlationId);
            await result.ExecuteAsync(context);
        }
        catch (PersonError error)
        {
            await ProblemResponse.WriteAsync(context, error, correlationId);
        }
    }
}

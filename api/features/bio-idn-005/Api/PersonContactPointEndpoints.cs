using System.Text.Json;
using Biolog.Features.DuplicateReview.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Biolog.Features.DuplicateReview.Api;

/// <summary>
/// BIO-IDN-005-QA-01's fix. <c>/api/v1/persons/{personId}/contact-point</c> (ADR-0004 §1.1
/// versioning) - tenant-plane, nested under the owning person, matching every other endpoint in
/// this project. The mutating route requires an asserted actor (<see cref="ActorResolver"/>).
/// </summary>
public static class PersonContactPointEndpoints
{
    private const string BasePath = "/api/v1/persons/{personId}/contact-point";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(BasePath);
        group.MapPut("", SetAsync);
        group.MapGet("", GetAsync);
    }

    private static Task SetAsync(HttpContext context, PersonContactPointService service, string personId) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        var request = await ReadJsonAsync<SetContactPointRequest>(context);
        var contactPoint = await service.SetAsync(personId, request, actor, correlationId, context.RequestAborted);
        return Results.Ok(PersonContactPointResponse.From(contactPoint));
    });

    private static Task GetAsync(HttpContext context, PersonContactPointService service, string personId) => Handle(context, async _ =>
    {
        var contactPoint = await service.GetAsync(personId, context.RequestAborted)
            ?? throw DuplicateCandidateError.ContactPointNotFound(personId);
        return Results.Ok(PersonContactPointResponse.From(contactPoint));
    });

    private static async Task<T> ReadJsonAsync<T>(HttpContext context)
    {
        try
        {
            var body = await JsonSerializer.DeserializeAsync<T>(context.Request.Body, DuplicateCandidateJsonOptions.Strict, context.RequestAborted);
            if (body is null)
            {
                throw DuplicateCandidateError.Validation([new ValidationErrorItem("", "required", "a JSON request body is required")]);
            }

            return body;
        }
        catch (JsonException ex)
        {
            throw DuplicateCandidateError.Validation([new ValidationErrorItem("", "invalid_format", $"request body is not valid JSON for this endpoint: {ex.Message}")]);
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
        catch (DuplicateCandidateError error)
        {
            await ProblemResponse.WriteAsync(context, error, correlationId);
        }
    }
}

using System.Text.Json;
using Biolog.Features.DuplicateReview.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Biolog.Features.DuplicateReview.Api;

/// <summary>
/// <c>/api/v1/persons/{personId}/duplicate-candidates</c> (ADR-0004 §1.1 versioning) -
/// tenant-plane, nested under the owning person, matching BIO-IDN-002/003's convention. Every
/// mutating route requires an asserted actor (<see cref="ActorResolver"/>).
/// </summary>
public static class DuplicateCandidateEndpoints
{
    private const string BasePath = "/api/v1/persons/{personId}/duplicate-candidates";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(BasePath);
        group.MapPost("/scan", ScanAsync);
        group.MapGet("", ListAsync);
        group.MapPost("/{otherPersonId}/decisions", DecideAsync);
    }

    /// <summary>BIO-IDN-005-AC01/AC02: staff-triggered comparison of <paramref name="personId"/> against every other person in the resolved tenant; every raised/augmented candidate is audited to the initiating actor.</summary>
    private static Task ScanAsync(HttpContext context, DuplicateReviewService service, string personId) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        var candidates = await service.ScanForPersonAsync(personId, actor, correlationId, context.RequestAborted);
        return Results.Ok(new DuplicateCandidateListResponse { Data = [.. candidates.Select(DuplicateCandidateResponse.From)] });
    });

    private static Task ListAsync(HttpContext context, DuplicateReviewService service, string personId) => Handle(context, async _ =>
    {
        var candidates = await service.ListForPersonAsync(personId, context.RequestAborted);
        return Results.Ok(new DuplicateCandidateListResponse { Data = [.. candidates.Select(DuplicateCandidateResponse.From)] });
    });

    /// <summary>BIO-IDN-005-AC02/AC03: records a human review decision. Never a merge - see Domain.DuplicateReviewStatus's header.</summary>
    private static Task DecideAsync(HttpContext context, DuplicateReviewService service, string personId, string otherPersonId) =>
        Handle(context, async correlationId =>
        {
            var actor = ActorResolver.Resolve(context.Request);
            var request = await ReadJsonAsync<DecideDuplicateCandidateRequest>(context);
            var candidate = await service.DecideAsync(personId, otherPersonId, request, actor, correlationId, context.RequestAborted);
            return Results.Ok(DuplicateCandidateResponse.From(candidate));
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

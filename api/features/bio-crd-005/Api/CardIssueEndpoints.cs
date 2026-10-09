using System.Text.Json;
using Biolog.Features.CardIssuance.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Biolog.Features.CardIssuance.Api;

/// <summary>
/// <c>/api/v1/persons/{personId}/card-issues/{cardClass}</c> (ADR-0004 §1.1 versioning),
/// nested under the owning person, matching BIO-IDN-002/003/006's convention. Every route
/// requires an asserted actor (<see cref="ActorResolver"/>) - none of these are anonymous.
///
/// <c>Activate</c> requires <c>Idempotency-Key</c> (ADR-0004 §4.3 names "card activation"
/// explicitly); no other route in this feature does.
/// </summary>
public static class CardIssueEndpoints
{
    public const string IdempotencyKeyHeaderName = "Idempotency-Key";
    private const string BasePath = "/api/v1/persons/{personId}/card-issues/{cardClass}";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(BasePath);
        group.MapPost("", IssueAsync);
        group.MapGet("", ListAsync);
        group.MapGet("/{sequenceNumber:int}", GetAsync);
        group.MapPost("/{sequenceNumber:int}/encode", EncodeAsync);
        group.MapPost("/{sequenceNumber:int}/print", PrintAsync);
        group.MapPost("/{sequenceNumber:int}/verify", VerifyAsync);
        group.MapPost("/{sequenceNumber:int}/activate", ActivateAsync);
        group.MapPost("/{sequenceNumber:int}/deactivate", DeactivateAsync);
    }

    private static Task IssueAsync(HttpContext context, CardIssueService service, string personId, string cardClass) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        var wire = await ReadJsonAsync<IssueCardWireRequest>(context);
        var request = new IssueCardRequest
        {
            TemplateRef = wire.TemplateRef,
            PhotoRef = wire.PhotoRef,
            LogoRef = wire.LogoRef,
            SignatureRef = wire.SignatureRef,
            PrinterRef = wire.PrinterRef,
        };
        var issue = await service.IssueAsync(personId, cardClass, request, actor, correlationId, context.RequestAborted);
        return Results.Created($"/api/v1/persons/{personId}/card-issues/{cardClass}/{issue.SequenceNumber}", CardIssueResponse.From(issue));
    });

    private static Task GetAsync(HttpContext context, CardIssueService service, string personId, string cardClass, int sequenceNumber) => Handle(context, async _ =>
    {
        var issue = await service.GetAsync(personId, cardClass, sequenceNumber, context.RequestAborted)
            ?? throw CardIssueError.CardIssueNotFound(personId, cardClass, sequenceNumber);
        return Results.Ok(CardIssueResponse.From(issue));
    });

    private static Task ListAsync(HttpContext context, CardIssueService service, string personId, string cardClass) => Handle(context, async _ =>
    {
        var issues = await service.ListAsync(personId, cardClass, context.RequestAborted);
        return Results.Ok(new CardIssueListResponse { Data = [.. issues.Select(CardIssueResponse.From)] });
    });

    private static Task EncodeAsync(HttpContext context, CardIssueService service, string personId, string cardClass, int sequenceNumber) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        var request = await ReadJsonAsync<CardIssueStepRequest>(context);
        var issue = await service.RecordEncodedAsync(personId, cardClass, sequenceNumber, request.ExpectedVersion, actor, correlationId, context.RequestAborted);
        return Results.Ok(CardIssueResponse.From(issue));
    });

    private static Task PrintAsync(HttpContext context, CardIssueService service, string personId, string cardClass, int sequenceNumber) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        var request = await ReadJsonAsync<CardIssueStepRequest>(context);
        var issue = await service.RecordPrintedAsync(personId, cardClass, sequenceNumber, request.ExpectedVersion, actor, correlationId, context.RequestAborted);
        return Results.Ok(CardIssueResponse.From(issue));
    });

    private static Task VerifyAsync(HttpContext context, CardIssueService service, string personId, string cardClass, int sequenceNumber) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        var request = await ReadJsonAsync<CardIssueStepRequest>(context);
        var issue = await service.RecordVerifiedAsync(personId, cardClass, sequenceNumber, request.ExpectedVersion, actor, correlationId, context.RequestAborted);
        return Results.Ok(CardIssueResponse.From(issue));
    });

    private static Task ActivateAsync(HttpContext context, CardIssueService service, string personId, string cardClass, int sequenceNumber) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        var request = await ReadJsonAsync<CardIssueStepRequest>(context);
        var idempotencyKey = context.Request.Headers[IdempotencyKeyHeaderName].ToString();
        var issue = await service.ActivateAsync(personId, cardClass, sequenceNumber, request.ExpectedVersion, idempotencyKey, actor, correlationId, context.RequestAborted);
        return Results.Ok(CardIssueResponse.From(issue));
    });

    private static Task DeactivateAsync(HttpContext context, CardIssueService service, string personId, string cardClass, int sequenceNumber) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        var request = await ReadJsonAsync<CardIssueStepRequest>(context);
        var issue = await service.DeactivateAsync(personId, cardClass, sequenceNumber, request.ExpectedVersion, actor, correlationId, context.RequestAborted);
        return Results.Ok(CardIssueResponse.From(issue));
    });

    private static async Task<T> ReadJsonAsync<T>(HttpContext context)
    {
        try
        {
            var body = await JsonSerializer.DeserializeAsync<T>(context.Request.Body, CardIssueJsonOptions.Strict, context.RequestAborted);
            if (body is null)
            {
                throw CardIssueError.Validation([new ValidationErrorItem("", "required", "a JSON request body is required")]);
            }

            return body;
        }
        catch (JsonException ex)
        {
            throw CardIssueError.Validation([new ValidationErrorItem("", "invalid_format", $"request body is not valid JSON for this endpoint: {ex.Message}")]);
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
        catch (CardIssueError error)
        {
            await ProblemResponse.WriteAsync(context, error, correlationId);
        }
    }
}

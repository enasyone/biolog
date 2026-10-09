using System.Text.Json;
using Biolog.Features.InstitutionalIdentifiers.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Biolog.Features.InstitutionalIdentifiers.Api;

/// <summary>
/// <c>/api/v1/persons/{personId}/identifiers</c> (ADR-0004 §1.1 versioning) - tenant-plane,
/// nested under the owning person, matching BIO-IDN-001/BIO-IDN-002's convention. Uniqueness
/// rule configuration (AC02) lives at the separate, tenant-scoped (not person-scoped)
/// <c>/api/v1/identifier-types/{type}/uniqueness-rule</c>. Every mutating route requires an
/// asserted actor (<see cref="ActorResolver"/>).
/// </summary>
public static class InstitutionalIdentifierEndpoints
{
    private const string IdentifiersBasePath = "/api/v1/persons/{personId}/identifiers";
    private const string RulesBasePath = "/api/v1/identifier-types/{type}/uniqueness-rule";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var identifiers = endpoints.MapGroup(IdentifiersBasePath);
        identifiers.MapPost("", AssignAsync);
        identifiers.MapGet("", ListAsync);
        identifiers.MapGet("/{type}", GetActiveAsync);
        identifiers.MapGet("/{type}/history", ListHistoryAsync);
        identifiers.MapPut("/{type}/{sequenceNumber}/retire", RetireAsync);

        var rules = endpoints.MapGroup(RulesBasePath);
        rules.MapGet("", GetRuleAsync);
        rules.MapPut("", SetRuleAsync);
    }

    private static Task AssignAsync(HttpContext context, InstitutionalIdentifierService service, string personId) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        var request = await ReadJsonAsync<AssignIdentifierRequest>(context);
        var identifier = await service.AssignAsync(personId, request, actor, correlationId, context.RequestAborted);
        return Results.Created(
            $"/api/v1/persons/{personId}/identifiers/{identifier.Type.ToWireString()}/{identifier.SequenceNumber}",
            InstitutionalIdentifierResponse.From(identifier));
    });

    private static Task ListAsync(HttpContext context, InstitutionalIdentifierService service, string personId) => Handle(context, async _ =>
    {
        var identifiers = await service.ListAsync(personId, context.RequestAborted);
        return Results.Ok(new InstitutionalIdentifierListResponse { Data = [.. identifiers.Select(InstitutionalIdentifierResponse.From)] });
    });

    private static Task GetActiveAsync(HttpContext context, InstitutionalIdentifierService service, string personId, string type) => Handle(context, async _ =>
    {
        var parsedType = ParseTypeOrThrow(type);
        var customLabel = ReadCustomLabel(context);
        var identifier = await service.GetActiveAsync(personId, parsedType, customLabel, context.RequestAborted)
            ?? throw InstitutionalIdentifierError.IdentifierNotFound(personId, parsedType, customLabel, sequenceNumber: 0);
        return Results.Ok(InstitutionalIdentifierResponse.From(identifier));
    });

    private static Task ListHistoryAsync(HttpContext context, InstitutionalIdentifierService service, string personId, string type) => Handle(context, async _ =>
    {
        var parsedType = ParseTypeOrThrow(type);
        var customLabel = ReadCustomLabel(context);
        var history = await service.ListHistoryAsync(personId, parsedType, customLabel, context.RequestAborted);
        return Results.Ok(new InstitutionalIdentifierListResponse { Data = [.. history.Select(InstitutionalIdentifierResponse.From)] });
    });

    private static Task RetireAsync(HttpContext context, InstitutionalIdentifierService service, string personId, string type, int sequenceNumber) =>
        Handle(context, async correlationId =>
        {
            var actor = ActorResolver.Resolve(context.Request);
            var parsedType = ParseTypeOrThrow(type);
            var customLabel = ReadCustomLabel(context);
            var request = await ReadJsonAsync<RetireIdentifierRequest>(context);
            var identifier = await service.RetireAsync(personId, parsedType, customLabel, sequenceNumber, request, actor, correlationId, context.RequestAborted);
            return Results.Ok(InstitutionalIdentifierResponse.From(identifier));
        });

    private static Task GetRuleAsync(HttpContext context, IdentifierUniquenessRuleService service, string type) => Handle(context, async _ =>
    {
        var parsedType = ParseTypeOrThrow(type);
        var customLabel = ReadCustomLabel(context);
        var rule = await service.GetRuleAsync(parsedType, customLabel, context.RequestAborted);
        return Results.Ok(IdentifierUniquenessRuleResponse.From(parsedType, customLabel, rule));
    });

    private static Task SetRuleAsync(HttpContext context, IdentifierUniquenessRuleService service, string type) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        var parsedType = ParseTypeOrThrow(type);
        var customLabel = ReadCustomLabel(context);
        var request = await ReadJsonAsync<SetUniquenessRuleRequest>(context);
        if (request.Enforced is not bool enforced)
        {
            throw InstitutionalIdentifierError.Validation([new ValidationErrorItem("enforced", "required", "enforced is required")]);
        }

        var rule = await service.SetRuleAsync(parsedType, customLabel, enforced, actor, correlationId, context.RequestAborted);
        return Results.Ok(IdentifierUniquenessRuleResponse.From(parsedType, customLabel, rule));
    });

    private static string? ReadCustomLabel(HttpContext context)
    {
        var value = context.Request.Query["custom_label"].ToString();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private static IdentifierType ParseTypeOrThrow(string type) =>
        IdentifierTypeExtensions.TryParse(type, out var parsed)
            ? parsed
            : throw InstitutionalIdentifierError.Validation(
                [new ValidationErrorItem("type", "invalid_format", "type must be one of MATRICULATION_NUMBER, STAFF_NUMBER, TENANT_DEFINED")]);

    private static async Task<T> ReadJsonAsync<T>(HttpContext context)
    {
        try
        {
            var body = await JsonSerializer.DeserializeAsync<T>(context.Request.Body, InstitutionalIdentifierJsonOptions.Strict, context.RequestAborted);
            if (body is null)
            {
                throw InstitutionalIdentifierError.Validation([new ValidationErrorItem("", "required", "a JSON request body is required")]);
            }

            return body;
        }
        catch (JsonException ex)
        {
            throw InstitutionalIdentifierError.Validation([new ValidationErrorItem("", "invalid_format", $"request body is not valid JSON for this endpoint: {ex.Message}")]);
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
        catch (InstitutionalIdentifierError error)
        {
            await ProblemResponse.WriteAsync(context, error, correlationId);
        }
    }
}

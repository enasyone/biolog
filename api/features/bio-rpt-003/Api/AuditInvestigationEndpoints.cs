using System.Text.Json;
using Biolog.Features.AuditInvestigation.Audit;
using Biolog.Features.AuditInvestigation.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Biolog.Features.AuditInvestigation.Api;

/// <summary>
/// One dedicated route group (BIO-RPT-003-AC01-AC03), entirely under its own path - checked
/// against the whole <c>api/</c> tree before mapping (the BIO-RPT-002/BIO-RPT-001 route-conflict
/// lesson): no other module maps anything under <c>/api/v1/audit-investigations</c>.
///
/// Deliberately GET + one POST only - no <c>MapPut</c>/<c>MapPatch</c>/<c>MapDelete</c> anywhere
/// under this prefix (AC02, "audit records are read-only to tenant users"; see
/// <c>AC02_no_mutation_route_exists_under_this_prefix</c> in the integration tests for the
/// structural regression check that keeps this true).
/// </summary>
public static class AuditInvestigationEndpoints
{
    public const string BasePath = "/api/v1/audit-investigations";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(BasePath);
        group.MapGet("/", SearchAsync);
        group.MapGet("/{eventId}", GetByIdAsync);
        group.MapPost("/export", ExportAsync);
    }

    private static Task SearchAsync(HttpContext context, AuditInvestigationService service) => Handle(context, async _ =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        var filter = ParseFilterFromQuery(context.Request);
        var (records, total) = await service.SearchAsync(filter, actor.Id, context.RequestAborted);
        return Results.Ok(new AuditInvestigationSearchResponse
        {
            Records = records.Select(AuditInvestigationRecordResponse.From).ToList(),
            TotalCount = total,
            Page = filter.Page,
            PageSize = filter.PageSize,
        });
    });

    private static Task GetByIdAsync(HttpContext context, AuditInvestigationService service, string eventId) => Handle(context, async _ =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        var record = await service.GetByIdAsync(eventId, actor.Id, context.RequestAborted);
        return record is null
            ? throw AuditInvestigationError.NotFound(eventId)
            : Results.Ok(AuditInvestigationRecordResponse.From(record));
    });

    private static Task ExportAsync(HttpContext context, AuditInvestigationService service) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        var request = await ReadJsonAsync<AuditInvestigationExportRequest>(context);
        var filter = ParseFilterFromExportRequest(request);
        var (bytes, contentType, fileName) = await service.ExportCsvAsync(filter, actor, correlationId, context.RequestAborted);
        return Results.File(bytes, contentType, fileName);
    });

    private static AuditInvestigationFilter ParseFilterFromQuery(HttpRequest request)
    {
        var query = request.Query;
        ActorType? actorType = null;
        if (!string.IsNullOrEmpty(query["actor_type"]) && ActorTypeExtensions.TryParse(query["actor_type"], out var parsedActorType))
        {
            actorType = parsedActorType;
        }

        return new AuditInvestigationFilter
        {
            ActorType = actorType,
            ActorId = NullIfEmpty(query["actor_id"]),
            PersonId = NullIfEmpty(query["person_id"]),
            Action = NullIfEmpty(query["action"]),
            DeviceId = NullIfEmpty(query["device_id"]),
            ReasonCode = NullIfEmpty(query["reason_code"]),
            OccurredFrom = NullIfEmpty(query["occurred_from"]),
            OccurredTo = NullIfEmpty(query["occurred_to"]),
            Page = int.TryParse(query["page"], out var page) && page > 0 ? page : 1,
            PageSize = int.TryParse(query["page_size"], out var pageSize) && pageSize > 0 ? pageSize : 50,
        };
    }

    private static AuditInvestigationFilter ParseFilterFromExportRequest(AuditInvestigationExportRequest request) => new()
    {
        ActorType = !string.IsNullOrEmpty(request.ActorType) && ActorTypeExtensions.TryParse(request.ActorType, out var actorType) ? actorType : null,
        ActorId = request.ActorId,
        PersonId = request.PersonId,
        Action = request.Action,
        DeviceId = request.DeviceId,
        ReasonCode = request.ReasonCode,
        OccurredFrom = request.OccurredFrom,
        OccurredTo = request.OccurredTo,
    };

    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;

    private static async Task<T> ReadJsonAsync<T>(HttpContext context) where T : new()
    {
        if (context.Request.ContentLength is null or 0)
        {
            return new T();
        }

        try
        {
            var parsed = await JsonSerializer.DeserializeAsync<T>(context.Request.Body, cancellationToken: context.RequestAborted);
            return parsed ?? new T();
        }
        catch (JsonException ex)
        {
            throw AuditInvestigationError.Validation([new ValidationErrorItem("", "invalid_format", $"request body is not valid JSON for this endpoint: {ex.Message}")]);
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
        catch (AuditInvestigationError error)
        {
            await ProblemResponse.WriteAsync(context, error, correlationId);
        }
    }
}

using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Biolog.Features.ImportConflictReview.Api;

/// <summary>
/// <c>/api/v1/integrations/mappings/{mappingId}/conflicts/*</c> (ADR-0004 §1.1 versioning),
/// tenant-plane. Deliberately nested under BIO-INT-002's own <c>/api/v1/integrations/mappings</c>
/// resource (never a new top-level resource) - a conflict review is always scoped to one
/// mapping's own import history, and nesting means this story's routes automatically fall under
/// the ALREADY-registered <c>web/lib/actor-provenance-routes.ts</c> entry for that prefix
/// (feature <c>Integrations</c>) with no route-table change needed - see
/// <see cref="ActorProvenanceGate"/>'s own header.
///
/// <b>ZB-INT005-01/ZB-INT005-02 (Zainab, High, round 1) - all three routes require both a
/// permission grant and a verified provenance proof.</b> An earlier version treated scan/list as
/// audit-attribution-only (ADR-0006 Decision 2), borrowing BIO-INT-002's own
/// <c>PreviewAsync</c>/<c>ListBatchesAsync</c> shape by analogy without re-justifying it against
/// THIS story's own actual side effects and data sensitivity - see
/// <see cref="ImportConflictReviewService.ReadPermission"/>'s own header for the full
/// correction. Scan genuinely mutates state (raises/augments/withdraws) and audits; list exposes
/// exact existing-vs-incoming identity field values and decision notes. Every route here now
/// requires <see cref="ImportConflictReviewService.ReadPermission"/> or
/// <see cref="ImportConflictReviewService.DecidePermission"/> (checked inside the service) plus
/// a verified <see cref="ActorProvenanceGate"/> proof (checked here, at the endpoint) - the
/// identical treatment BIO-TEN-003's own <c>TenantFeatureConfigurationEndpoints</c> already
/// gives its own GET route, never exempting a sensitive read just because it is a read.
/// </summary>
public static class ImportConflictReviewEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var conflicts = endpoints.MapGroup("/api/v1/integrations/mappings/{mappingId}/conflicts");
        conflicts.MapPost("/scan", ScanAsync);
        conflicts.MapGet("", ListAsync);
        conflicts.MapPost("/{externalKey}/decisions", DecideAsync);
    }

    private static Task ScanAsync(HttpContext context, ImportConflictReviewService service, ActorProvenanceGate provenanceGate, string mappingId) =>
        Handle(context, async correlationId =>
        {
            var actor = ActorResolver.Resolve(context.Request);
            provenanceGate.Require(context.Request, actor.Id);
            var reviews = await service.ScanAsync(mappingId, actor, correlationId, context.RequestAborted);
            return Results.Ok(new { data = reviews.Select(ImportConflictReviewResponse.From).ToList() });
        });

    private static Task ListAsync(HttpContext context, ImportConflictReviewService service, ActorProvenanceGate provenanceGate, string mappingId) =>
        Handle(context, async _ =>
        {
            var actor = ActorResolver.Resolve(context.Request);
            provenanceGate.Require(context.Request, actor.Id);
            var reviews = await service.ListForMappingAsync(mappingId, actor, context.RequestAborted);
            return Results.Ok(new { data = reviews.Select(ImportConflictReviewResponse.From).ToList() });
        });

    private static Task DecideAsync(HttpContext context, ImportConflictReviewService service, ActorProvenanceGate provenanceGate, string mappingId, string externalKey) =>
        Handle(context, async correlationId =>
        {
            var actor = ActorResolver.Resolve(context.Request);
            provenanceGate.Require(context.Request, actor.Id);
            var request = await ReadJsonAsync<DecideImportConflictReviewRequest>(context);
            var review = await service.DecideAsync(mappingId, externalKey, request, actor, correlationId, context.RequestAborted);
            return Results.Ok(ImportConflictReviewResponse.From(review));
        });

    private static async Task<T> ReadJsonAsync<T>(HttpContext context)
    {
        try
        {
            var body = await JsonSerializer.DeserializeAsync<T>(context.Request.Body, ImportConflictReviewJsonOptions.Strict, context.RequestAborted);
            if (body is null)
            {
                throw Domain.ImportConflictReviewError.Validation([new ValidationErrorItem("", "required", "a JSON request body is required")]);
            }

            return body;
        }
        catch (JsonException ex)
        {
            throw Domain.ImportConflictReviewError.Validation([new ValidationErrorItem("", "invalid_format", $"request body is not valid JSON for this endpoint: {ex.Message}")]);
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
        catch (Domain.ImportConflictReviewError error)
        {
            await ProblemResponse.WriteAsync(context, error, correlationId);
        }
    }
}

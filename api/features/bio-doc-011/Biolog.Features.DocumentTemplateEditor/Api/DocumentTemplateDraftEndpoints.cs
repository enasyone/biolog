using System.Text.Json;
using Biolog.Features.DocumentTemplateEditor.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Biolog.Features.DocumentTemplateEditor.Api;

/// <summary>
/// One tenant-level route group for the document-template-draft lifecycle (BIO-DOC-011-AC01-AC05),
/// entirely under its own dedicated paths - no other module maps anything under
/// <c>/api/v1/document-template-drafts</c>. There is deliberately no frontend route wired to
/// these endpoints: see <c>docs/features/bio-doc-011/DOCUMENT-TEMPLATE-EDITOR.md</c>'s own
/// "Why this story ships no new web UI" section - no admin editor route exists anywhere in
/// <c>governance/ui-integration-map.json</c> or <c>web/lib/route-manifest.ts</c> for this
/// capability, unlike BIO-CRD-008's own lifecycle bar, which had an already-approved
/// <c>/card-designer</c> canvas (built years earlier by BIO-CRD-002/003/004) to attach to.
/// </summary>
public static class DocumentTemplateDraftEndpoints
{
    private const string BasePath = "/api/v1/document-template-drafts";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(BasePath);
        group.MapPost("/", CreateDraftAsync);
        group.MapGet("/{templateId}/versions", ListVersionsAsync);
        group.MapGet("/{templateId}/versions/{version:int}", GetVersionAsync);
        group.MapGet("/{templateId}/published", GetPublishedAsync);
        group.MapPost("/{templateId}/versions/{version:int}/reference-layout", AttachReferenceLayoutAsync);
        group.MapPost("/{templateId}/versions/{version:int}/regions", DefineRegionsAsync);
        group.MapPost("/{templateId}/versions/{version:int}/submit-for-test", SubmitForTestAsync);
        group.MapPost("/{templateId}/versions/{version:int}/corpus-runs", RunCorpusAsync);
        group.MapPost("/{templateId}/versions/{version:int}/publish", PublishAsync);
        group.MapPost("/{templateId}/versions/{version:int}/new-version", CreateNextVersionAsync);
    }

    private static Task CreateDraftAsync(HttpContext context, DocumentTemplateDraftService service, ActorProvenanceGate provenanceGate) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        provenanceGate.Require(context.Request, actor.Id);
        var request = await ReadJsonAsync<CreateDraftRequest>(context);
        var draft = await service.CreateDraftAsync(request.Name, actor, correlationId, context.RequestAborted);
        return Results.Created($"{BasePath}/{draft.TemplateId}/versions/{draft.Version}", DocumentTemplateDraftResponse.From(draft));
    });

    /// <summary>Seyi F01 / ZB-DOC011-03: reads are tenant-scoped administrative template content, not public reference data (unlike BIO-DOC-010's own published pack) - a verified provenance proof alone is not authorization, so this now also requires <see cref="DocumentTemplateDraftService.ReadPermission"/> (or either mutation permission) via <see cref="DocumentTemplateDraftService.ListVersionsAsync"/>.</summary>
    private static Task ListVersionsAsync(HttpContext context, DocumentTemplateDraftService service, ActorProvenanceGate provenanceGate, string templateId) => Handle(context, async _ =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        provenanceGate.Require(context.Request, actor.Id);
        var versions = await service.ListVersionsAsync(templateId, actor.Id, context.RequestAborted);
        return Results.Ok(new { versions = versions.Select(DocumentTemplateDraftResponse.From).ToList() });
    });

    private static Task GetVersionAsync(HttpContext context, DocumentTemplateDraftService service, ActorProvenanceGate provenanceGate, string templateId, int version) => Handle(context, async _ =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        provenanceGate.Require(context.Request, actor.Id);
        var draft = await service.GetVersionAsync(templateId, version, actor.Id, context.RequestAborted);
        return Results.Ok(DocumentTemplateDraftResponse.From(draft));
    });

    private static Task GetPublishedAsync(HttpContext context, DocumentTemplateDraftService service, ActorProvenanceGate provenanceGate, string templateId) => Handle(context, async _ =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        provenanceGate.Require(context.Request, actor.Id);
        var published = await service.GetPublishedAsync(templateId, actor.Id, context.RequestAborted) ?? throw DocumentTemplateDraftError.NoPublishedVersion(templateId);
        return Results.Ok(DocumentTemplateDraftResponse.From(published));
    });

    private static Task AttachReferenceLayoutAsync(HttpContext context, DocumentTemplateDraftService service, ActorProvenanceGate provenanceGate, string templateId, int version) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        provenanceGate.Require(context.Request, actor.Id);
        var request = await ReadJsonAsync<AttachReferenceLayoutRequest>(context);

        byte[] content;
        try
        {
            content = Convert.FromBase64String(request.ContentBase64);
        }
        catch (FormatException)
        {
            throw DocumentTemplateDraftError.Validation([new ValidationErrorItem("content_base64", "invalid_format", "content_base64 is not valid base64")]);
        }

        var draft = await service.AttachReferenceLayoutAsync(templateId, version, request.Width, request.Height, content, request.ExpectedRevision, actor, correlationId, context.RequestAborted);
        return Results.Ok(DocumentTemplateDraftResponse.From(draft));
    });

    private static Task DefineRegionsAsync(HttpContext context, DocumentTemplateDraftService service, ActorProvenanceGate provenanceGate, string templateId, int version) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        provenanceGate.Require(context.Request, actor.Id);
        var request = await ReadJsonAsync<DefineRegionsRequest>(context);
        var fieldRegions = ParseFieldRegions(request.FieldRegions);
        var graphicRegions = request.GraphicRegions.Select(g => new DocumentTemplateGraphicRegion(g.Name, g.Left, g.Top, g.Width, g.Height)).ToList();

        var draft = await service.DefineRegionsAsync(templateId, version, fieldRegions, graphicRegions, request.ExpectedRevision, actor, correlationId, context.RequestAborted);
        return Results.Ok(DocumentTemplateDraftResponse.From(draft));
    });

    private static Task SubmitForTestAsync(HttpContext context, DocumentTemplateDraftService service, ActorProvenanceGate provenanceGate, string templateId, int version) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        provenanceGate.Require(context.Request, actor.Id);
        var request = await ReadJsonAsync<RevisionOnlyRequest>(context);
        var draft = await service.SubmitForTestAsync(templateId, version, request.ExpectedRevision, actor, correlationId, context.RequestAborted);
        return Results.Ok(DocumentTemplateDraftResponse.From(draft));
    });

    private static Task RunCorpusAsync(HttpContext context, DocumentTemplateDraftService service, ActorProvenanceGate provenanceGate, string templateId, int version) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        provenanceGate.Require(context.Request, actor.Id);
        var request = await ReadJsonAsync<RunCorpusRequest>(context);
        var samples = request.Samples
            .Select(s => new DocumentTemplateCorpusSample(s.SampleId, s.ExpectedFieldValues, s.CorruptFieldNames is null ? null : new HashSet<string>(s.CorruptFieldNames, StringComparer.Ordinal)))
            .ToList();

        var draft = await service.RunCorpusAsync(templateId, version, samples, request.ExpectedRevision, actor, correlationId, context.RequestAborted);
        return Results.Ok(DocumentTemplateDraftResponse.From(draft));
    });

    private static Task PublishAsync(HttpContext context, DocumentTemplateDraftService service, ActorProvenanceGate provenanceGate, string templateId, int version) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        provenanceGate.Require(context.Request, actor.Id);
        var request = await ReadJsonAsync<RevisionOnlyRequest>(context);
        var draft = await service.PublishAsync(templateId, version, request.ExpectedRevision, actor, correlationId, context.RequestAborted);
        return Results.Ok(DocumentTemplateDraftResponse.From(draft));
    });

    private static Task CreateNextVersionAsync(HttpContext context, DocumentTemplateDraftService service, ActorProvenanceGate provenanceGate, string templateId, int version) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        provenanceGate.Require(context.Request, actor.Id);
        var draft = await service.CreateNextVersionAsync(templateId, version, actor, correlationId, context.RequestAborted);
        return Results.Created($"{BasePath}/{draft.TemplateId}/versions/{draft.Version}", DocumentTemplateDraftResponse.From(draft));
    });

    private static List<Biolog.Features.OcrEngine.Model.OcrTemplateField> ParseFieldRegions(IReadOnlyList<FieldRegionDto> dtos)
    {
        var errors = new List<ValidationErrorItem>();
        var fields = new List<Biolog.Features.OcrEngine.Model.OcrTemplateField>(dtos.Count);

        foreach (var dto in dtos)
        {
            if (!Enum.TryParse<Biolog.Features.OcrEngine.Model.OcrFieldType>(dto.FieldType, ignoreCase: true, out var fieldType))
            {
                errors.Add(new ValidationErrorItem("field_regions.field_type", "invalid", $"'{dto.FieldType}' is not a recognised field type"));
                continue;
            }

            fields.Add(new Biolog.Features.OcrEngine.Model.OcrTemplateField(dto.FieldName, fieldType, dto.Left, dto.Top, dto.CharacterCount, dto.Critical));
        }

        if (errors.Count > 0)
        {
            throw DocumentTemplateDraftError.Validation(errors);
        }

        return fields;
    }

    private static async Task<T> ReadJsonAsync<T>(HttpContext context)
    {
        try
        {
            var body = await JsonSerializer.DeserializeAsync<T>(context.Request.Body, DocumentTemplateDraftJsonOptions.Strict, context.RequestAborted);
            if (body is null)
            {
                throw DocumentTemplateDraftError.Validation([new ValidationErrorItem("", "required", "a JSON request body is required")]);
            }

            return body;
        }
        catch (JsonException ex)
        {
            throw DocumentTemplateDraftError.Validation([new ValidationErrorItem("", "invalid_format", $"request body is not valid JSON for this endpoint: {ex.Message}")]);
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
        catch (DocumentTemplateDraftError error)
        {
            await ProblemResponse.WriteAsync(context, error, correlationId);
        }
        catch (ActorProvenanceRequiredException ex)
        {
            await ProblemResponse.WriteAsync(context, new DocumentTemplateDraftError(ex.Message, "actor_provenance_required", StatusCodes.Status401Unauthorized), correlationId);
        }
    }
}

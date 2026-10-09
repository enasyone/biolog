using System.Text.Json;
using Biolog.Features.CardTemplates.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Biolog.Features.CardTemplates.Api;

/// <summary>
/// One tenant-level route group for the card-template lifecycle (BIO-CRD-008-AC01-AC04), entirely
/// under its own dedicated paths - checked against the whole <c>api/</c> tree before mapping
/// (the BIO-RPT-002/BIO-RPT-001 route-conflict lesson): no other module maps anything under
/// <c>/api/v1/card-templates</c>.
/// </summary>
public static class CardTemplateEndpoints
{
    private const string BasePath = "/api/v1/card-templates";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(BasePath);
        group.MapPost("/", CreateDraftAsync);
        group.MapGet("/", ListLineagesAsync);
        group.MapGet("/{templateId}/versions", ListVersionsAsync);
        group.MapGet("/{templateId}/versions/{version:int}", GetVersionAsync);
        group.MapGet("/{templateId}/published", GetPublishedAsync);
        group.MapPut("/{templateId}/versions/{version:int}", EditAsync);
        group.MapPost("/{templateId}/versions/{version:int}/submit", SubmitAsync);
        group.MapPost("/{templateId}/versions/{version:int}/validate", ValidateAsync);
        group.MapPost("/{templateId}/versions/{version:int}/reject", RejectAsync);
        group.MapPost("/{templateId}/versions/{version:int}/publish", PublishAsync);
        group.MapPost("/{templateId}/versions/{version:int}/retire", RetireAsync);
        group.MapPost("/{templateId}/versions/{version:int}/new-version", CreateNextVersionAsync);
    }

    private static Task CreateDraftAsync(HttpContext context, CardTemplateService service) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        var request = await ReadJsonAsync<CreateDraftRequest>(context);
        var snapshot = ParseSnapshot(request.Snapshot);
        var template = await service.CreateDraftAsync(request.Name, snapshot, actor, correlationId, context.RequestAborted);
        return Results.Created($"{BasePath}/{template.TemplateId}/versions/{template.Version}", CardTemplateResponse.From(template));
    });

    private static Task ListLineagesAsync(HttpContext context, CardTemplateService service) => Handle(context, async _ =>
        Results.Ok(new { lineages = service.ListLineages().Select(CardTemplateLineageSummaryResponse.From).ToList() }));

    private static Task ListVersionsAsync(HttpContext context, CardTemplateService service, string templateId) => Handle(context, async _ =>
        Results.Ok(new { versions = service.ListVersions(templateId).Select(CardTemplateResponse.From).ToList() }));

    private static Task GetVersionAsync(HttpContext context, CardTemplateService service, string templateId, int version) => Handle(context, async _ =>
        Results.Ok(CardTemplateResponse.From(service.GetVersion(templateId, version))));

    private static Task GetPublishedAsync(HttpContext context, CardTemplateService service, string templateId) => Handle(context, async _ =>
        Results.Ok(CardTemplateResponse.From(service.GetPublished(templateId))));

    private static Task EditAsync(HttpContext context, CardTemplateService service, string templateId, int version) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        var request = await ReadJsonAsync<EditRequest>(context);
        var snapshot = ParseSnapshot(request.Snapshot);
        var template = await service.EditAsync(templateId, version, request.Name, snapshot, request.ExpectedRevision, actor, correlationId, context.RequestAborted);
        return Results.Ok(CardTemplateResponse.From(template));
    });

    private static Task SubmitAsync(HttpContext context, CardTemplateService service, string templateId, int version) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        var request = await ReadJsonAsync<RevisionOnlyRequest>(context);
        var template = await service.SubmitForValidationAsync(templateId, version, request.ExpectedRevision, actor, correlationId, context.RequestAborted);
        return Results.Ok(CardTemplateResponse.From(template));
    });

    private static Task ValidateAsync(HttpContext context, CardTemplateService service, string templateId, int version) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        var request = await ReadJsonAsync<RecordValidationRequest>(context);
        var template = await service.RecordValidationAsync(templateId, version, request.Passed, request.Note, request.ExpectedRevision, actor, correlationId, context.RequestAborted);
        return Results.Ok(CardTemplateResponse.From(template));
    });

    private static Task RejectAsync(HttpContext context, CardTemplateService service, string templateId, int version) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        var request = await ReadJsonAsync<RejectRequest>(context);
        var template = await service.RejectAsync(templateId, version, request.Note, request.ExpectedRevision, actor, correlationId, context.RequestAborted);
        return Results.Ok(CardTemplateResponse.From(template));
    });

    private static Task PublishAsync(HttpContext context, CardTemplateService service, string templateId, int version) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        var request = await ReadJsonAsync<RevisionOnlyRequest>(context);
        var template = await service.PublishAsync(templateId, version, request.ExpectedRevision, actor, correlationId, context.RequestAborted);
        return Results.Ok(CardTemplateResponse.From(template));
    });

    private static Task RetireAsync(HttpContext context, CardTemplateService service, string templateId, int version) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        var request = await ReadJsonAsync<RevisionOnlyRequest>(context);
        var template = await service.RetireAsync(templateId, version, request.ExpectedRevision, actor, correlationId, context.RequestAborted);
        return Results.Ok(CardTemplateResponse.From(template));
    });

    private static Task CreateNextVersionAsync(HttpContext context, CardTemplateService service, string templateId, int version) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        var request = await ReadJsonAsync<CreateNextVersionRequest>(context);
        var snapshot = request.Snapshot is null ? null : ParseSnapshot(request.Snapshot);
        var template = await service.CreateNextVersionAsync(templateId, version, snapshot, actor, correlationId, context.RequestAborted);
        return Results.Created($"{BasePath}/{template.TemplateId}/versions/{template.Version}", CardTemplateResponse.From(template));
    });

    private static CardTemplateSnapshot ParseSnapshot(CardTemplateSnapshotDto dto)
    {
        var errors = new List<ValidationErrorItem>();

        if (!CardFormatExtensions.TryParse(dto.FormatKind, out var formatKind))
        {
            errors.Add(new ValidationErrorItem("snapshot.format_kind", "invalid", $"'{dto.FormatKind}' is not a recognised format"));
        }

        if (!CardFormatExtensions.TryParseOrientation(dto.Orientation, out var orientation))
        {
            errors.Add(new ValidationErrorItem("snapshot.orientation", "invalid", $"'{dto.Orientation}' is not a recognised orientation"));
        }

        if (!WatermarkPresetExtensions.TryParse(dto.Watermark, out var watermark))
        {
            errors.Add(new ValidationErrorItem("snapshot.watermark", "invalid", $"'{dto.Watermark}' is not a recognised watermark preset"));
        }

        if (!BarcodeProfileExtensions.TryParse(dto.Barcode, out var barcode))
        {
            errors.Add(new ValidationErrorItem("snapshot.barcode", "invalid", $"'{dto.Barcode}' is not a recognised barcode profile"));
        }

        if (errors.Count > 0)
        {
            throw CardTemplateError.Validation(errors);
        }

        return new CardTemplateSnapshot
        {
            FormatKind = formatKind,
            Orientation = orientation,
            CustomSizeMm = dto.CustomSizeMm is null ? null : new CustomSizeMm(dto.CustomSizeMm.LongEdgeMm, dto.CustomSizeMm.ShortEdgeMm),
            FrontElements = [.. dto.FrontElements.Select(ParseElement)],
            BackElements = [.. dto.BackElements.Select(ParseElement)],
            Watermark = watermark,
            Photo = new PhotoSpec { Required = dto.Photo.Required, AspectRatio = dto.Photo.AspectRatio, MinWidthPx = dto.Photo.MinWidthPx, MinHeightPx = dto.Photo.MinHeightPx },
            Barcode = barcode,
            Signature = new SignatureRule { Required = dto.Signature.Required, LineageId = dto.Signature.LineageId, Version = dto.Signature.Version },
        };
    }

    private static CardTemplateElement ParseElement(CardTemplateElementDto dto)
    {
        if (!Enum.TryParse<CardTemplateElementKind>(dto.Kind, ignoreCase: true, out var kind))
        {
            throw CardTemplateError.Validation([new ValidationErrorItem("snapshot.elements.kind", "invalid", $"'{dto.Kind}' is not a recognised element kind")]);
        }

        return new CardTemplateElement
        {
            Id = dto.Id,
            Kind = kind,
            Label = dto.Label,
            XPx = dto.XPx,
            YPx = dto.YPx,
            WidthPx = dto.WidthPx,
            HeightPx = dto.HeightPx,
            RotationDeg = dto.RotationDeg,
            ZIndex = dto.ZIndex,
            Locked = dto.Locked,
            FontFamily = dto.FontFamily,
            FontSizePx = dto.FontSizePx,
            Text = dto.Text,
        };
    }

    private static async Task<T> ReadJsonAsync<T>(HttpContext context)
    {
        try
        {
            var body = await JsonSerializer.DeserializeAsync<T>(context.Request.Body, CardTemplateJsonOptions.Strict, context.RequestAborted);
            if (body is null)
            {
                throw CardTemplateError.Validation([new ValidationErrorItem("", "required", "a JSON request body is required")]);
            }

            return body;
        }
        catch (JsonException ex)
        {
            throw CardTemplateError.Validation([new ValidationErrorItem("", "invalid_format", $"request body is not valid JSON for this endpoint: {ex.Message}")]);
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
        catch (CardTemplateError error)
        {
            await ProblemResponse.WriteAsync(context, error, correlationId);
        }
    }
}

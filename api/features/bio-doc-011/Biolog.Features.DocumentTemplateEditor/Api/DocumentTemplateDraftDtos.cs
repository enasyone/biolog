using System.Text.Json;
using System.Text.Json.Serialization;
using Biolog.Features.DocumentTemplateEditor.Domain;
using Biolog.Features.OcrEngine.Model;

namespace Biolog.Features.DocumentTemplateEditor.Api;

public static class DocumentTemplateDraftJsonOptions
{
    public static readonly JsonSerializerOptions Strict = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
}

public sealed record CreateDraftRequest
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }
}

public sealed record AttachReferenceLayoutRequest
{
    [JsonPropertyName("width")]
    public required int Width { get; init; }

    [JsonPropertyName("height")]
    public required int Height { get; init; }

    /// <summary>Base64-encoded synthetic content. Hashed server-side (never decoded as an image - no image library exists anywhere in this repo).</summary>
    [JsonPropertyName("content_base64")]
    public required string ContentBase64 { get; init; }

    [JsonPropertyName("expected_revision")]
    public required int ExpectedRevision { get; init; }
}

public sealed record FieldRegionDto
{
    [JsonPropertyName("field_name")]
    public required string FieldName { get; init; }

    [JsonPropertyName("field_type")]
    public required string FieldType { get; init; }

    [JsonPropertyName("left")]
    public required int Left { get; init; }

    [JsonPropertyName("top")]
    public required int Top { get; init; }

    [JsonPropertyName("character_count")]
    public required int CharacterCount { get; init; }

    [JsonPropertyName("critical")]
    public bool Critical { get; init; }
}

public sealed record GraphicRegionDto
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("left")]
    public required int Left { get; init; }

    [JsonPropertyName("top")]
    public required int Top { get; init; }

    [JsonPropertyName("width")]
    public required int Width { get; init; }

    [JsonPropertyName("height")]
    public required int Height { get; init; }
}

public sealed record DefineRegionsRequest
{
    [JsonPropertyName("field_regions")]
    public IReadOnlyList<FieldRegionDto> FieldRegions { get; init; } = [];

    [JsonPropertyName("graphic_regions")]
    public IReadOnlyList<GraphicRegionDto> GraphicRegions { get; init; } = [];

    [JsonPropertyName("expected_revision")]
    public required int ExpectedRevision { get; init; }
}

public sealed record RevisionOnlyRequest
{
    [JsonPropertyName("expected_revision")]
    public required int ExpectedRevision { get; init; }
}

public sealed record CorpusSampleDto
{
    [JsonPropertyName("sample_id")]
    public required string SampleId { get; init; }

    [JsonPropertyName("expected_field_values")]
    public required IReadOnlyDictionary<string, string> ExpectedFieldValues { get; init; }

    [JsonPropertyName("corrupt_field_names")]
    public IReadOnlyList<string>? CorruptFieldNames { get; init; }
}

public sealed record RunCorpusRequest
{
    [JsonPropertyName("samples")]
    public required IReadOnlyList<CorpusSampleDto> Samples { get; init; }

    [JsonPropertyName("expected_revision")]
    public required int ExpectedRevision { get; init; }
}

public sealed record FieldRegionResponse
{
    [JsonPropertyName("field_name")]
    public required string FieldName { get; init; }

    [JsonPropertyName("field_type")]
    public required string FieldType { get; init; }

    [JsonPropertyName("left")]
    public required int Left { get; init; }

    [JsonPropertyName("top")]
    public required int Top { get; init; }

    [JsonPropertyName("character_count")]
    public required int CharacterCount { get; init; }

    [JsonPropertyName("critical")]
    public required bool Critical { get; init; }

    public static FieldRegionResponse From(OcrTemplateField field) => new()
    {
        FieldName = field.FieldName,
        FieldType = field.FieldType.ToString(),
        Left = field.Left,
        Top = field.Top,
        CharacterCount = field.CharacterCount,
        Critical = field.Critical,
    };
}

public sealed record GraphicRegionResponse
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("left")]
    public required int Left { get; init; }

    [JsonPropertyName("top")]
    public required int Top { get; init; }

    [JsonPropertyName("width")]
    public required int Width { get; init; }

    [JsonPropertyName("height")]
    public required int Height { get; init; }

    public static GraphicRegionResponse From(DocumentTemplateGraphicRegion region) => new()
    {
        Name = region.Name,
        Left = region.Left,
        Top = region.Top,
        Width = region.Width,
        Height = region.Height,
    };
}

public sealed record ReferenceLayoutResponse
{
    [JsonPropertyName("width")]
    public required int Width { get; init; }

    [JsonPropertyName("height")]
    public required int Height { get; init; }

    [JsonPropertyName("content_hash")]
    public required string ContentHash { get; init; }

    [JsonPropertyName("uploaded_at")]
    public required string UploadedAt { get; init; }

    public static ReferenceLayoutResponse From(DocumentTemplateReferenceLayout layout) => new()
    {
        Width = layout.Width,
        Height = layout.Height,
        ContentHash = layout.ContentHash,
        UploadedAt = layout.UploadedAt,
    };
}

public sealed record CorpusRunResultResponse
{
    [JsonPropertyName("passed")]
    public required bool Passed { get; init; }

    [JsonPropertyName("sample_count")]
    public required int SampleCount { get; init; }

    [JsonPropertyName("failure_reasons")]
    public required IReadOnlyList<string> FailureReasons { get; init; }

    [JsonPropertyName("ran_at")]
    public required string RanAt { get; init; }

    public static CorpusRunResultResponse From(DocumentTemplateCorpusRunResult result) => new()
    {
        Passed = result.Passed,
        SampleCount = result.SampleCount,
        FailureReasons = result.FailureReasons,
        RanAt = result.RanAt,
    };
}

/// <summary>AC04: <see cref="PublicReference"/> is the <c>tenant:</c>-prefixed identifier proving tenant (never global-pack) origin - see <see cref="DocumentTemplateDraft"/>'s own header.</summary>
public sealed record DocumentTemplateDraftResponse
{
    [JsonPropertyName("public_reference")]
    public required string PublicReference { get; init; }

    [JsonPropertyName("template_id")]
    public required string TemplateId { get; init; }

    [JsonPropertyName("version")]
    public required int Version { get; init; }

    [JsonPropertyName("revision")]
    public required int Revision { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("reference_layout")]
    public ReferenceLayoutResponse? ReferenceLayout { get; init; }

    [JsonPropertyName("field_regions")]
    public required IReadOnlyList<FieldRegionResponse> FieldRegions { get; init; }

    [JsonPropertyName("graphic_regions")]
    public required IReadOnlyList<GraphicRegionResponse> GraphicRegions { get; init; }

    [JsonPropertyName("last_corpus_run")]
    public CorpusRunResultResponse? LastCorpusRun { get; init; }

    [JsonPropertyName("created_at")]
    public required string CreatedAt { get; init; }

    [JsonPropertyName("updated_at")]
    public required string UpdatedAt { get; init; }

    [JsonPropertyName("published_at")]
    public string? PublishedAt { get; init; }

    public static DocumentTemplateDraftResponse From(DocumentTemplateDraft draft) => new()
    {
        PublicReference = draft.PublicReference,
        TemplateId = draft.TemplateId,
        Version = draft.Version,
        Revision = draft.Revision,
        Status = draft.Status.ToWireString(),
        Name = draft.Name,
        ReferenceLayout = draft.ReferenceLayout is null ? null : ReferenceLayoutResponse.From(draft.ReferenceLayout),
        FieldRegions = [.. draft.FieldRegions.Select(FieldRegionResponse.From)],
        GraphicRegions = [.. draft.GraphicRegions.Select(GraphicRegionResponse.From)],
        LastCorpusRun = draft.LastCorpusRun is null ? null : CorpusRunResultResponse.From(draft.LastCorpusRun),
        CreatedAt = draft.CreatedAt,
        UpdatedAt = draft.UpdatedAt,
        PublishedAt = draft.PublishedAt,
    };
}

using System.Text.Json;
using System.Text.Json.Serialization;
using Biolog.Features.CardTemplates.Audit;
using Biolog.Features.CardTemplates.Domain;

namespace Biolog.Features.CardTemplates.Api;

public static class CardTemplateJsonOptions
{
    public static readonly JsonSerializerOptions Strict = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
}

public sealed record CustomSizeMmDto
{
    [JsonPropertyName("long_edge_mm")]
    public required double LongEdgeMm { get; init; }

    [JsonPropertyName("short_edge_mm")]
    public required double ShortEdgeMm { get; init; }
}

public sealed record CardTemplateElementDto
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("kind")]
    public required string Kind { get; init; }

    [JsonPropertyName("label")]
    public required string Label { get; init; }

    [JsonPropertyName("x_px")]
    public required double XPx { get; init; }

    [JsonPropertyName("y_px")]
    public required double YPx { get; init; }

    [JsonPropertyName("width_px")]
    public required double WidthPx { get; init; }

    [JsonPropertyName("height_px")]
    public required double HeightPx { get; init; }

    [JsonPropertyName("rotation_deg")]
    public double RotationDeg { get; init; }

    [JsonPropertyName("z_index")]
    public required int ZIndex { get; init; }

    [JsonPropertyName("locked")]
    public bool Locked { get; init; }

    [JsonPropertyName("font_family")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FontFamily { get; init; }

    [JsonPropertyName("font_size_px")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? FontSizePx { get; init; }

    [JsonPropertyName("text")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Text { get; init; }

    public static CardTemplateElementDto From(CardTemplateElement element) => new()
    {
        Id = element.Id,
        Kind = element.Kind.ToString().ToLowerInvariant(),
        Label = element.Label,
        XPx = element.XPx,
        YPx = element.YPx,
        WidthPx = element.WidthPx,
        HeightPx = element.HeightPx,
        RotationDeg = element.RotationDeg,
        ZIndex = element.ZIndex,
        Locked = element.Locked,
        FontFamily = element.FontFamily,
        FontSizePx = element.FontSizePx,
        Text = element.Text,
    };
}

public sealed record PhotoSpecDto
{
    [JsonPropertyName("required")]
    public required bool Required { get; init; }

    [JsonPropertyName("aspect_ratio")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AspectRatio { get; init; }

    [JsonPropertyName("min_width_px")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MinWidthPx { get; init; }

    [JsonPropertyName("min_height_px")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MinHeightPx { get; init; }
}

public sealed record SignatureRuleDto
{
    [JsonPropertyName("required")]
    public required bool Required { get; init; }

    [JsonPropertyName("lineage_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LineageId { get; init; }

    [JsonPropertyName("version")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Version { get; init; }
}

public sealed record CardTemplateSnapshotDto
{
    [JsonPropertyName("format_kind")]
    public required string FormatKind { get; init; }

    [JsonPropertyName("orientation")]
    public required string Orientation { get; init; }

    [JsonPropertyName("custom_size_mm")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CustomSizeMmDto? CustomSizeMm { get; init; }

    [JsonPropertyName("front_elements")]
    public IReadOnlyList<CardTemplateElementDto> FrontElements { get; init; } = [];

    [JsonPropertyName("back_elements")]
    public IReadOnlyList<CardTemplateElementDto> BackElements { get; init; } = [];

    [JsonPropertyName("watermark")]
    public string Watermark { get; init; } = "none";

    [JsonPropertyName("photo")]
    public PhotoSpecDto Photo { get; init; } = new() { Required = false };

    [JsonPropertyName("barcode")]
    public string Barcode { get; init; } = "none";

    [JsonPropertyName("signature")]
    public SignatureRuleDto Signature { get; init; } = new() { Required = false };

    public static CardTemplateSnapshotDto From(CardTemplateSnapshot snapshot) => new()
    {
        FormatKind = snapshot.FormatKind.ToWireString(),
        Orientation = snapshot.Orientation.ToWireString(),
        CustomSizeMm = snapshot.CustomSizeMm is null ? null : new CustomSizeMmDto { LongEdgeMm = snapshot.CustomSizeMm.LongEdgeMm, ShortEdgeMm = snapshot.CustomSizeMm.ShortEdgeMm },
        FrontElements = [.. snapshot.FrontElements.Select(CardTemplateElementDto.From)],
        BackElements = [.. snapshot.BackElements.Select(CardTemplateElementDto.From)],
        Watermark = snapshot.Watermark.ToWireString(),
        Photo = new PhotoSpecDto { Required = snapshot.Photo.Required, AspectRatio = snapshot.Photo.AspectRatio, MinWidthPx = snapshot.Photo.MinWidthPx, MinHeightPx = snapshot.Photo.MinHeightPx },
        Barcode = snapshot.Barcode.ToWireString(),
        Signature = new SignatureRuleDto { Required = snapshot.Signature.Required, LineageId = snapshot.Signature.LineageId, Version = snapshot.Signature.Version },
    };
}

public sealed record AuditActorDto
{
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("id")]
    public required string Id { get; init; }
}

public sealed record CardTemplateTransitionDto
{
    [JsonPropertyName("from_status")]
    public required string FromStatus { get; init; }

    [JsonPropertyName("to_status")]
    public required string ToStatus { get; init; }

    [JsonPropertyName("actor")]
    public required AuditActorDto Actor { get; init; }

    [JsonPropertyName("at")]
    public required string At { get; init; }

    [JsonPropertyName("note")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Note { get; init; }
}

public sealed record CardTemplateResponse
{
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

    [JsonPropertyName("snapshot")]
    public required CardTemplateSnapshotDto Snapshot { get; init; }

    [JsonPropertyName("created_at")]
    public required string CreatedAt { get; init; }

    [JsonPropertyName("updated_at")]
    public required string UpdatedAt { get; init; }

    [JsonPropertyName("published_at")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PublishedAt { get; init; }

    [JsonPropertyName("transitions")]
    public required IReadOnlyList<CardTemplateTransitionDto> Transitions { get; init; }

    public static CardTemplateResponse From(CardTemplate template) => new()
    {
        TemplateId = template.TemplateId,
        Version = template.Version,
        Revision = template.Revision,
        Status = template.Status.ToWireString(),
        Name = template.Name,
        Snapshot = CardTemplateSnapshotDto.From(template.Snapshot),
        CreatedAt = template.CreatedAt,
        UpdatedAt = template.UpdatedAt,
        PublishedAt = template.PublishedAt,
        Transitions = [.. template.Transitions.Select(t => new CardTemplateTransitionDto
        {
            FromStatus = t.FromStatus.ToWireString(),
            ToStatus = t.ToStatus.ToWireString(),
            Actor = new AuditActorDto { Type = t.Actor.Type.ToWireString(), Id = t.Actor.Id },
            At = t.At,
            Note = t.Note,
        })],
    };
}

public sealed record CardTemplateLineageSummaryResponse
{
    [JsonPropertyName("template_id")]
    public required string TemplateId { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("latest_version")]
    public required int LatestVersion { get; init; }

    [JsonPropertyName("latest_status")]
    public required string LatestStatus { get; init; }

    [JsonPropertyName("published_version")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? PublishedVersion { get; init; }

    public static CardTemplateLineageSummaryResponse From(CardTemplateLineageSummary summary) => new()
    {
        TemplateId = summary.TemplateId,
        Name = summary.Name,
        LatestVersion = summary.LatestVersion,
        LatestStatus = summary.LatestStatus.ToWireString(),
        PublishedVersion = summary.PublishedVersion,
    };
}

public sealed record CreateDraftRequest
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("snapshot")]
    public required CardTemplateSnapshotDto Snapshot { get; init; }
}

public sealed record EditRequest
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("snapshot")]
    public required CardTemplateSnapshotDto Snapshot { get; init; }

    [JsonPropertyName("expected_revision")]
    public required int ExpectedRevision { get; init; }
}

public sealed record RevisionOnlyRequest
{
    [JsonPropertyName("expected_revision")]
    public required int ExpectedRevision { get; init; }
}

public sealed record RecordValidationRequest
{
    [JsonPropertyName("passed")]
    public required bool Passed { get; init; }

    [JsonPropertyName("note")]
    public string? Note { get; init; }

    [JsonPropertyName("expected_revision")]
    public required int ExpectedRevision { get; init; }
}

public sealed record RejectRequest
{
    [JsonPropertyName("note")]
    public string? Note { get; init; }

    [JsonPropertyName("expected_revision")]
    public required int ExpectedRevision { get; init; }
}

public sealed record CreateNextVersionRequest
{
    [JsonPropertyName("snapshot")]
    public CardTemplateSnapshotDto? Snapshot { get; init; }
}

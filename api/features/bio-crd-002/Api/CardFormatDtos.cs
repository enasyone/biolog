using System.Text.Json.Serialization;
using Biolog.Features.CardDesigner.Domain;

namespace Biolog.Features.CardDesigner.Api;

public sealed record CardFormatEntry
{
    [JsonPropertyName("kind")]
    public required string Kind { get; init; }

    [JsonPropertyName("long_edge_mm")]
    public required double LongEdgeMm { get; init; }

    [JsonPropertyName("short_edge_mm")]
    public required double ShortEdgeMm { get; init; }
}

public sealed record CustomSizeBounds
{
    [JsonPropertyName("min_edge_mm")]
    public required double MinEdgeMm { get; init; }

    [JsonPropertyName("max_edge_mm")]
    public required double MaxEdgeMm { get; init; }
}

/// <summary>BIO-CRD-002-AC01/AC03: the full catalogue a caller needs in one read - every standard format's physical dimensions, plus the bounds a custom size must satisfy.</summary>
public sealed record CardFormatCatalogResponse
{
    [JsonPropertyName("data")]
    public required IReadOnlyList<CardFormatEntry> Data { get; init; }

    [JsonPropertyName("custom_size_bounds")]
    public required CustomSizeBounds CustomSizeBounds { get; init; }

    public static CardFormatCatalogResponse Build() => new()
    {
        Data = [.. CardFormatCatalog.Standard.Select(pair => new CardFormatEntry
        {
            Kind = pair.Key.ToWireString(),
            LongEdgeMm = pair.Value.LongEdgeMm,
            ShortEdgeMm = pair.Value.ShortEdgeMm,
        })],
        CustomSizeBounds = new CustomSizeBounds
        {
            MinEdgeMm = CustomCardSizeValidator.MinEdgeMm,
            MaxEdgeMm = CustomCardSizeValidator.MaxEdgeMm,
        },
    };
}

/// <summary>Body of <c>POST /api/v1/card-designer/custom-size/validate</c>.</summary>
public sealed record ValidateCustomSizeRequest
{
    [JsonPropertyName("width_mm")]
    public required double WidthMm { get; init; }

    [JsonPropertyName("height_mm")]
    public required double HeightMm { get; init; }
}

/// <summary>
/// BIO-CRD-002-AC03: the verdict is DATA, not an HTTP error - asking "is this size valid?" and
/// getting back "no, and here is why" is the endpoint working correctly, not failing. Only a
/// malformed request BODY (missing/non-numeric fields) is a real <c>validation_failed</c> 422.
/// </summary>
public sealed record ValidateCustomSizeResponse
{
    [JsonPropertyName("is_valid")]
    public required bool IsValid { get; init; }

    [JsonPropertyName("errors")]
    public required IReadOnlyList<string> Errors { get; init; }

    public static ValidateCustomSizeResponse From(CustomCardSizeValidator.ValidationResult result) => new()
    {
        IsValid = result.IsValid,
        Errors = result.Errors,
    };
}

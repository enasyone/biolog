using System.Text.Json.Serialization;
using Biolog.Features.CardTypography.Domain;

namespace Biolog.Features.CardTypography.Api;

public sealed record WatermarkPresetEntry
{
    [JsonPropertyName("kind")]
    public required string Kind { get; init; }

    [JsonPropertyName("text")]
    public required string Text { get; init; }

    [JsonPropertyName("default_opacity_percent")]
    public required double DefaultOpacityPercent { get; init; }

    [JsonPropertyName("default_rotation_deg")]
    public required double DefaultRotationDeg { get; init; }
}

/// <summary>BIO-CRD-003-AC05: the full starter-watermark catalogue in one read.</summary>
public sealed record WatermarkPresetCatalogResponse
{
    [JsonPropertyName("data")]
    public required IReadOnlyList<WatermarkPresetEntry> Data { get; init; }

    public static WatermarkPresetCatalogResponse Build() => new()
    {
        Data = [.. WatermarkPresetCatalog.Presets.Select(pair => new WatermarkPresetEntry
        {
            Kind = pair.Key.ToWireString(),
            Text = pair.Value.Text,
            DefaultOpacityPercent = pair.Value.DefaultOpacityPercent,
            DefaultRotationDeg = pair.Value.DefaultRotationDeg,
        })],
    };
}

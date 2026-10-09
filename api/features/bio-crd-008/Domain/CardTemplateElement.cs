namespace Biolog.Features.CardTemplates.Domain;

/// <summary>AC03: "retains ... objects/fonts ... assets." Mirrors BIO-CRD-002's own <c>CanvasElement</c> geometry (<c>web/features/bio-crd-002/elements.ts</c>) plus the font fields no existing frontend canvas element carries yet - this snapshot is the first place in the codebase a template's typography is recorded alongside its geometry.</summary>
/// <remarks>BIO-CRD-009-AC04: <see cref="Photo"/> is a purely additive member so a placed photo element captures the same way a logo already does (<c>Image</c>) - parsed generically by the existing <c>Enum.TryParse&lt;CardTemplateElementKind&gt;</c> call in <c>CardTemplateEndpoints.cs</c>, no other backend change needed.</remarks>
public enum CardTemplateElementKind
{
    Text,
    Image,
    Watermark,
    Photo,
}

public sealed record CardTemplateElement
{
    public required string Id { get; init; }
    public required CardTemplateElementKind Kind { get; init; }
    public required string Label { get; init; }
    public required double XPx { get; init; }
    public required double YPx { get; init; }
    public required double WidthPx { get; init; }
    public required double HeightPx { get; init; }
    public double RotationDeg { get; init; }
    public required int ZIndex { get; init; }
    public bool Locked { get; init; }

    /// <summary>Set only when <see cref="Kind"/> is <see cref="CardTemplateElementKind.Text"/>.</summary>
    public string? FontFamily { get; init; }
    public double? FontSizePx { get; init; }
    public string? Text { get; init; }
}

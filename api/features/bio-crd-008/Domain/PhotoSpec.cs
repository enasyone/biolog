namespace Biolog.Features.CardTemplates.Domain;

/// <summary>
/// AC03: "retains ... photo spec." Not a cross-story reference (no BIO-PHO-00x dependency is
/// declared for this story) - a template only records the CAPTURE requirement a photo must
/// satisfy to be placed on it, never a specific photo asset itself.
/// </summary>
public sealed record PhotoSpec
{
    public required bool Required { get; init; }

    /// <summary>e.g. "3:4", "1:1". Ignored when <see cref="Required"/> is false.</summary>
    public string? AspectRatio { get; init; }

    public int? MinWidthPx { get; init; }
    public int? MinHeightPx { get; init; }

    public static PhotoSpec NotRequired { get; } = new() { Required = false };
}

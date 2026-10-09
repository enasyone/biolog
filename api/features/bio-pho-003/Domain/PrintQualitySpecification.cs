namespace Biolog.Features.PhotoQuality.Domain;

/// <summary>
/// BIO-PHO-003-AC01's print-quality side. No master-spec section or ADR names concrete values
/// for frame size/aspect ratio/minimum DPI at the time of writing, so <see cref="Current"/> is
/// this story's own, deliberately named baseline - one place to revise, not a magic number
/// scattered through the feature, the same resolution BIO-PHO-002's own
/// <c>CaptureSpecification</c> used for an identical unspecified gap. Reuses BIO-PHO-002's
/// exact values rather than independently re-deriving them: both stories target the same
/// passport/ID-card-photo domain (a 35mm x 45mm frame at 300 DPI, ICAO-style), and BIO-PHO-002's
/// own baseline already fetched by booths in production - a second, differently-tuned print
/// specification for the same physical card slot would be an undocumented, confusing
/// divergence, not a deliberate design choice.
/// </summary>
public sealed record PrintQualitySpecification
{
    public required int FrameWidthPx { get; init; }
    public required int FrameHeightPx { get; init; }
    public required int AspectRatioWidth { get; init; }
    public required int AspectRatioHeight { get; init; }
    public required int MinimumDpi { get; init; }

    public static readonly PrintQualitySpecification Current = new()
    {
        FrameWidthPx = 1050, // 35mm at 300dpi (rounded up from 413.4) - matches BIO-PHO-002's CaptureSpecification.Current
        FrameHeightPx = 1350, // 45mm at 300dpi (rounded up from 531.5)
        AspectRatioWidth = 7,
        AspectRatioHeight = 9, // 35:45 reduces to 7:9
        MinimumDpi = 300,
    };
}

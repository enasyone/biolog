namespace Biolog.Features.Photography.Domain;

/// <summary>
/// Computes a <see cref="DerivativeKind"/>-appropriate rendition from an immutable
/// <see cref="PhotoMaster"/>. The seam a future story swaps for real pixel resampling without
/// changing <see cref="PhotoDerivativeService"/> or anything above it.
/// </summary>
public interface IPhotoDerivativeGenerator
{
    (PhotoFormat Format, int WidthPx, int HeightPx, byte[] Content) Generate(PhotoMaster master, DerivativeKind kind);
}

/// <summary>
/// <b>Disclosed scope boundary (read before calling this "resizing").</b> No image-processing
/// library (SixLabors.ImageSharp, SkiaSharp, System.Drawing) is referenced anywhere in this
/// host yet, and adding one is outside this story's own declared scope without further
/// specification (Card Designer / photo-quality follow-on work is BIO-PHO-003, a later,
/// separately-scoped story per <c>governance/feature-path-ownership.json</c>). This
/// implementation computes the real TARGET dimensions a print or display rendition should have
/// (see <see cref="TargetSize"/>) and records them faithfully, but it does not decode, resample
/// or re-encode the master's pixels - <see cref="Generate"/> carries the master's own bytes
/// through unchanged. A later story that adds real resampling implements this same interface
/// and is a drop-in replacement; nothing above this seam - <see cref="PhotoDerivativeService"/>,
/// the repository, the endpoints - changes shape when that happens.
///
/// This is the same "compute real metadata, do not fabricate a capability nobody asked for and
/// no library exists to provide" boundary <c>RasterImageInspector</c> (BIO-CRD-001) already
/// established for DPI warnings without pixel resampling.
/// </summary>
public sealed class MetadataOnlyPhotoDerivativeGenerator : IPhotoDerivativeGenerator
{
    public const string GeneratorTag = "metadata-only-v1";

    // Conventional target sizes: not specified anywhere in the repository at the time of
    // writing (no master spec section or ADR names a pixel target for either derivative use).
    // Chosen as reasonable, named constants - one place to revise, not a magic number
    // scattered through the feature - matching BIO-CRD-001's identical resolution for its own
    // unspecified DPI floor.
    private const int PrintTargetWidthPx = 1200;
    private const int PrintTargetHeightPx = 1500; // 4:5, a conventional ID-photo print aspect ratio
    private const int DisplayTargetWidthPx = 320;
    private const int DisplayTargetHeightPx = 400;

    public (PhotoFormat Format, int WidthPx, int HeightPx, byte[] Content) Generate(PhotoMaster master, DerivativeKind kind)
    {
        var (targetWidth, targetHeight) = TargetSize(kind);
        return (master.Format, targetWidth, targetHeight, master.Content);
    }

    private static (int WidthPx, int HeightPx) TargetSize(DerivativeKind kind) => kind switch
    {
        DerivativeKind.Print => (PrintTargetWidthPx, PrintTargetHeightPx),
        DerivativeKind.Display => (DisplayTargetWidthPx, DisplayTargetHeightPx),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "unreachable: exhaustive switch"),
    };
}

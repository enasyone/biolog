namespace Biolog.Features.BarcodeEngine.Model;

/// <summary>
/// A single-plane (ink / no-ink) pixel grid representing a page image (BIO-DOC-003-AC01/AC02),
/// the same native, dependency-free boundary BIO-DOC-002's <c>DocumentRaster</c> is - duplicated
/// here rather than referenced, because this story's only declared dependency is BIO-DOC-001, not
/// BIO-DOC-002, and a shared common raster type does not exist in <c>contracts/</c> or elsewhere;
/// inventing one unilaterally would be exactly the kind of shared/common-primitive change that
/// needs Kemi's approval, not a decision this story makes on its own. No image-decoding library
/// exists anywhere in this repo (checked: no SkiaSharp/ImageSharp/OpenCvSharp/System.Drawing/
/// ML.NET reference in any <c>.csproj</c>); real container-format decoding into this grid is a
/// scanner/camera-integration concern for a later story.
/// </summary>
public sealed class BarcodeRaster
{
    private readonly bool[,] _ink;

    public int Width { get; }
    public int Height { get; }

    private BarcodeRaster(int width, int height, bool[,] ink)
    {
        Width = width;
        Height = height;
        _ink = ink;
    }

    /// <summary>True if the pixel at (x, y) carries ink. Out-of-range coordinates are always
    /// "no ink" - a detector scanning a fixed-size window past the raster edge sees blank, not an
    /// exception.</summary>
    public bool HasInkAt(int x, int y)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height)
        {
            return false;
        }

        return _ink[y, x];
    }

    public static BarcodeRaster Create(int width, int height, bool[,] ink)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), width, "must be > 0");
        }
        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), height, "must be > 0");
        }
        if (ink.GetLength(0) != height || ink.GetLength(1) != width)
        {
            throw new ArgumentException(
                $"ink array shape ({ink.GetLength(1)}x{ink.GetLength(0)}) must match ({width}x{height})",
                nameof(ink));
        }

        return new BarcodeRaster(width, height, (bool[,])ink.Clone());
    }

    public static BarcodeRaster Blank(int width, int height) => Create(width, height, new bool[height, width]);
}

namespace Biolog.Features.MrzEngine.Model;

/// <summary>
/// A single-plane (ink / no-ink) pixel grid representing a page image (BIO-DOC-002-AC01: "MRZ
/// region can be detected from synthetic/reference images"). This is deliberately NOT a decoded
/// JPEG/PNG: no image-decoding library exists anywhere in this repo (confirmed: no SkiaSharp,
/// ImageSharp, OpenCvSharp, System.Drawing or ML.NET reference in any .csproj), and
/// <c>DocumentEvidence</c> (BIO-DOC-001) itself never stores decoded pixel bytes, only a SHA-256
/// hash and MIME type (ADR-0003 Decision 5: "evidence references, not evidence copies"). Turning
/// real container-format bytes into a pixel grid is a scanner/camera-integration concern for a
/// later story; this type is the native, dependency-free boundary this engine operates on,
/// matching the "synthetic data only" builder constraint and the precedent set by
/// <c>api/Biolog.Api/Features/BrandAssets/RasterImageInspector.cs</c> (dependency-free, minimal,
/// no full pixel decode) - see docs/features/bio-doc-002/MRZ-ENGINE.md.
/// </summary>
public sealed class DocumentRaster
{
    private readonly bool[,] _ink;

    public int Width { get; }
    public int Height { get; }

    private DocumentRaster(int width, int height, bool[,] ink)
    {
        Width = width;
        Height = height;
        _ink = ink;
    }

    /// <summary>True if the pixel at (x, y) carries ink (dark/printed). Out-of-range coordinates
    /// are always "no ink" - a detector scanning a fixed-size window past the raster edge should
    /// see blank, not an exception.</summary>
    public bool HasInkAt(int x, int y)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height)
        {
            return false;
        }

        return _ink[y, x];
    }

    /// <summary>True if any pixel in row <paramref name="y"/> carries ink.</summary>
    public bool RowHasInk(int y)
    {
        if (y < 0 || y >= Height)
        {
            return false;
        }

        for (var x = 0; x < Width; x++)
        {
            if (_ink[y, x])
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>True if any row in [<paramref name="top"/>, <paramref name="top"/> +
    /// <paramref name="height"/>) carries ink at column <paramref name="x"/>.</summary>
    public bool ColumnHasInk(int x, int top, int height)
    {
        for (var y = top; y < top + height; y++)
        {
            if (HasInkAt(x, y))
            {
                return true;
            }
        }

        return false;
    }

    public static DocumentRaster Create(int width, int height, bool[,] ink)
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

        var copy = (bool[,])ink.Clone();
        return new DocumentRaster(width, height, copy);
    }

    /// <summary>An all-blank raster of the given size (e.g. a page with no MRZ present at all -
    /// the AC05 "nothing detected" negative case).</summary>
    public static DocumentRaster Blank(int width, int height) => Create(width, height, new bool[height, width]);
}

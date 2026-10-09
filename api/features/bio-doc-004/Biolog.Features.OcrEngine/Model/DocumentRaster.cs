namespace Biolog.Features.OcrEngine.Model;

/// <summary>
/// A single-plane (ink / no-ink) pixel grid representing a page image (BIO-DOC-004-AC01/AC02),
/// the same native, dependency-free boundary BIO-DOC-002's <c>DocumentRaster</c> and
/// BIO-DOC-003's <c>BarcodeRaster</c> are - duplicated here rather than referenced, because this
/// story's only declared dependency is BIO-DOC-001, not its document-intelligence siblings, and
/// no shared common raster type exists in <c>contracts/</c>. No image-decoding library exists
/// anywhere in this repo; real container-format decoding into this grid is a scanner/camera-
/// integration concern for a later story.
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

    public bool HasInkAt(int x, int y)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height)
        {
            return false;
        }

        return _ink[y, x];
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

        return new DocumentRaster(width, height, (bool[,])ink.Clone());
    }

    public static DocumentRaster Blank(int width, int height) => Create(width, height, new bool[height, width]);
}

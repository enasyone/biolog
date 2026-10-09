using Biolog.Features.MrzEngine.Model;

namespace Biolog.Features.MrzEngine.Tests.Fixtures;

/// <summary>
/// Test-only synthetic page builder (BIO-DOC-002's builder constraint: "use synthetic data
/// only"), mirroring how
/// <c>tests/dotnet/Biolog.UnitTests/Features/BrandAssets/SyntheticImages.cs</c> builds fixtures
/// for <c>RasterImageInspector.cs</c> without itself being production code. Stamps
/// <see cref="MrzGlyphFont"/>'s own glyph templates onto a mutable canvas so a test can assemble
/// a realistic page (an MRZ block, plus optional non-text "photo" ink blocks, noise, or
/// deliberately corrupted glyph cells) and hand the immutable result to
/// <see cref="MrzReader.Read"/> exactly as production code would receive it.
/// </summary>
public sealed class SyntheticRasterCanvas
{
    private readonly bool[,] _ink;

    public int Width { get; }
    public int Height { get; }

    public SyntheticRasterCanvas(int width, int height)
    {
        Width = width;
        Height = height;
        _ink = new bool[height, width];
    }

    public void SetPixel(int x, int y, bool ink)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height)
        {
            return;
        }

        _ink[y, x] = ink;
    }

    public void FillRect(int left, int top, int width, int height, bool ink = true)
    {
        for (var y = top; y < top + height; y++)
        {
            for (var x = left; x < left + width; x++)
            {
                SetPixel(x, y, ink);
            }
        }
    }

    public void DrawGlyph(char character, int left, int top)
    {
        var template = MrzGlyphFont.Glyphs[character];
        for (var y = 0; y < MrzGlyphFont.GlyphHeight; y++)
        {
            for (var x = 0; x < MrzGlyphFont.GlyphWidth; x++)
            {
                SetPixel(left + x, top + y, template[y, x]);
            }
        }
    }

    /// <summary>Draws each string in <paramref name="lines"/> as one MRZ text line, starting at
    /// (<paramref name="left"/>, <paramref name="top"/>), at the engine's fixed glyph pitch.
    /// Returns the top row of the line immediately after the block, so callers can chain more
    /// content below it.</summary>
    public int DrawMrzLines(IReadOnlyList<string> lines, int left, int top)
    {
        for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
        {
            var line = lines[lineIndex];
            var lineTop = top + lineIndex * MrzGlyphFont.CellPitchY;
            for (var col = 0; col < line.Length; col++)
            {
                DrawGlyph(line[col], left + col * MrzGlyphFont.CellPitchX, lineTop);
            }
        }

        return top + lines.Count * MrzGlyphFont.CellPitchY;
    }

    /// <summary>Paints a raw bitmap (any size) directly, overwriting whatever was already drawn
    /// at that position - used to inject a deliberately corrupted glyph cell over one already
    /// drawn by <see cref="DrawMrzLines"/> (AC05 low-confidence tests).</summary>
    public void DrawBitmap(bool[,] bitmap, int left, int top)
    {
        for (var y = 0; y < bitmap.GetLength(0); y++)
        {
            for (var x = 0; x < bitmap.GetLength(1); x++)
            {
                SetPixel(left + x, top + y, bitmap[y, x]);
            }
        }
    }

    public DocumentRaster ToRaster() => DocumentRaster.Create(Width, Height, _ink);
}

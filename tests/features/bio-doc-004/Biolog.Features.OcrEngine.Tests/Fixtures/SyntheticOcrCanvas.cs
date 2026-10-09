using Biolog.Features.OcrEngine.Model;

namespace Biolog.Features.OcrEngine.Tests.Fixtures;

/// <summary>Test-only synthetic page builder, mirroring BIO-DOC-002's <c>SyntheticRasterCanvas</c>
/// and BIO-DOC-003's <c>SyntheticBarcodeCanvas</c> (BIO-DOC-004's builder constraint: "use
/// synthetic data only").</summary>
public sealed class SyntheticOcrCanvas
{
    private readonly bool[,] _ink;

    public int Width { get; }
    public int Height { get; }

    public SyntheticOcrCanvas(int width, int height)
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
        var template = OcrGlyphFont.Glyphs[character];
        for (var y = 0; y < OcrGlyphFont.GlyphHeight; y++)
        {
            for (var x = 0; x < OcrGlyphFont.GlyphWidth; x++)
            {
                SetPixel(left + x, top + y, template[y, x]);
            }
        }
    }

    /// <summary>Draws <paramref name="text"/> as one line at the engine's fixed glyph pitch.
    /// Returns the left column immediately after the last character.</summary>
    public int DrawText(string text, int left, int top)
    {
        var x = left;
        foreach (var c in text)
        {
            DrawGlyph(c, x, top);
            x += OcrGlyphFont.CellPitchX;
        }

        return x;
    }

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

using Biolog.Features.OcrEngine.Model;

namespace Biolog.Features.DocumentClassifier.Tests.Fixtures;

/// <summary>Own local copy of BIO-DOC-004's synthetic OCR canvas, trimmed to what this story's
/// classifier tests need: drawing text to obtain a real <see cref="TemplateOcrResult"/> via
/// <c>TemplateOcrEngine.Read</c>.</summary>
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

    private void SetPixel(int x, int y, bool ink)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height)
        {
            return;
        }

        _ink[y, x] = ink;
    }

    private void DrawGlyph(char character, int left, int top)
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

    public void DrawText(string text, int left, int top)
    {
        var x = left;
        foreach (var c in text)
        {
            DrawGlyph(c, x, top);
            x += OcrGlyphFont.CellPitchX;
        }
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

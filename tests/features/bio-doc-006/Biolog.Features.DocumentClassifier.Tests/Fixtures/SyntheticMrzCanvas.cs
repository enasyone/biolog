using Biolog.Features.MrzEngine.Model;

namespace Biolog.Features.DocumentClassifier.Tests.Fixtures;

/// <summary>
/// Test-only synthetic page builder for this story's own tests, mirroring BIO-DOC-002's
/// <c>SyntheticRasterCanvas</c> (own local copy rather than a cross-test-project reference - the
/// same "each feature owns its own small fixture" precedent every sibling engine's own test
/// project already follows). Trimmed to only what BIO-DOC-006's classifier tests actually need:
/// drawing MRZ text lines to obtain a real <see cref="MrzReadOutcome"/> via <c>MrzReader.Read</c>.
/// </summary>
public sealed class SyntheticMrzCanvas
{
    private readonly bool[,] _ink;

    public int Width { get; }
    public int Height { get; }

    public SyntheticMrzCanvas(int width, int height)
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
        var template = MrzGlyphFont.Glyphs[character];
        for (var y = 0; y < MrzGlyphFont.GlyphHeight; y++)
        {
            for (var x = 0; x < MrzGlyphFont.GlyphWidth; x++)
            {
                SetPixel(left + x, top + y, template[y, x]);
            }
        }
    }

    public void DrawMrzLines(IReadOnlyList<string> lines, int left, int top)
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
    }

    public DocumentRaster ToRaster() => DocumentRaster.Create(Width, Height, _ink);
}

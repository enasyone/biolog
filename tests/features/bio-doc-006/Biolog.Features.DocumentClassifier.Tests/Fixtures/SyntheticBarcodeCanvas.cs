using Biolog.Features.BarcodeEngine.Model;

namespace Biolog.Features.DocumentClassifier.Tests.Fixtures;

/// <summary>Own local copy of BIO-DOC-003's synthetic barcode canvas, trimmed to Code 39 - the one
/// symbology BIO-DOC-003 fully decodes payload bytes for - which is all this story's classifier
/// tests need to obtain a real <see cref="BarcodeDecodeResult"/>.</summary>
public sealed class SyntheticBarcodeCanvas
{
    private readonly bool[,] _ink;

    public int Width { get; }
    public int Height { get; }

    public SyntheticBarcodeCanvas(int width, int height)
    {
        Width = width;
        Height = height;
        _ink = new bool[height, width];
    }

    private void FillRect(int left, int top, int width, int height, bool ink = true)
    {
        for (var y = top; y < top + height; y++)
        {
            for (var x = left; x < left + width; x++)
            {
                if (x < 0 || x >= Width || y < 0 || y >= Height)
                {
                    continue;
                }

                _ink[y, x] = ink;
            }
        }
    }

    public void DrawCode39(string payload, int left, int top)
    {
        var x = left;
        x = DrawCode39Character(Code39Table.StartStop, x, top);
        x += Code39Decoder.ModulePx;

        foreach (var c in payload)
        {
            x = DrawCode39Character(c, x, top);
            x += Code39Decoder.ModulePx;
        }

        DrawCode39Character(Code39Table.StartStop, x, top);
    }

    private int DrawCode39Character(char character, int left, int top)
    {
        var pattern = Code39Table.Patterns[character];
        var x = left;
        for (var i = 0; i < pattern.Length; i++)
        {
            var isBar = i % 2 == 0;
            var widthPx = (pattern[i] ? 2 : 1) * Code39Decoder.ModulePx;
            if (isBar)
            {
                FillRect(x, top, widthPx, Code39Decoder.BarHeightPx, ink: true);
            }

            x += widthPx;
        }

        return x;
    }

    public BarcodeRaster ToRaster() => BarcodeRaster.Create(Width, Height, _ink);
}

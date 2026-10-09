using Biolog.Features.BarcodeEngine.Model;

namespace Biolog.Features.BarcodeEngine.Tests.Fixtures;

/// <summary>
/// Test-only synthetic page builder (BIO-DOC-003's builder constraint: "use synthetic data
/// only"), mirroring BIO-DOC-002's <c>SyntheticRasterCanvas</c>. A mutable ink canvas that
/// symbology-specific render helpers stamp onto, producing an immutable <see cref="BarcodeRaster"/>
/// exactly as production code would receive one.
/// </summary>
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

    /// <summary>Renders a Code 39 payload (start/stop <c>*</c> added automatically) as a row of
    /// bars at <see cref="Code39Decoder.ModulePx"/> pitch and <see cref="Code39Decoder.BarHeightPx"/>
    /// height, using the exact same <see cref="Code39Table.Patterns"/> table the decoder matches
    /// against.</summary>
    public int DrawCode39(string payload, int left, int top)
    {
        var x = left;
        x = DrawCode39Character(Code39Table.StartStop, x, top);
        x += Code39Decoder.ModulePx; // inter-character gap

        foreach (var c in payload)
        {
            x = DrawCode39Character(c, x, top);
            x += Code39Decoder.ModulePx;
        }

        x = DrawCode39Character(Code39Table.StartStop, x, top);
        return x;
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

    /// <summary>Renders a row whose bar/space runs deliberately take on
    /// <paramref name="moduleWidths"/>'s exact module counts (1-4), the real structural signature
    /// <see cref="Code128SignatureDecoder"/> looks for - not a genuine Code 128 character table
    /// encode (see that decoder's remarks on why).</summary>
    public int DrawVariableWidthRow(IReadOnlyList<int> moduleWidths, int left, int top, int heightPx)
    {
        var x = left;
        for (var i = 0; i < moduleWidths.Count; i++)
        {
            var isBar = i % 2 == 0;
            var widthPx = moduleWidths[i] * Code128SignatureDecoder.ModulePx;
            if (isBar)
            {
                FillRect(x, top, widthPx, heightPx, ink: true);
            }

            x += widthPx;
        }

        return x;
    }

    /// <summary>Renders the real QR finder pattern (7x7 modules) at
    /// <see cref="QrSignatureDecoder.ModulePx"/> scale.</summary>
    public void DrawQrFinderPattern(int left, int top)
    {
        string[] rows =
        [
            "1111111",
            "1000001",
            "1011101",
            "1011101",
            "1011101",
            "1000001",
            "1111111",
        ];

        for (var my = 0; my < rows.Length; my++)
        {
            for (var mx = 0; mx < rows[my].Length; mx++)
            {
                FillRect(
                    left + mx * QrSignatureDecoder.ModulePx,
                    top + my * QrSignatureDecoder.ModulePx,
                    QrSignatureDecoder.ModulePx,
                    QrSignatureDecoder.ModulePx,
                    ink: rows[my][mx] == '1');
            }
        }
    }

    /// <summary>Renders a Data Matrix-style "L" finder: solid left column + bottom row, alternating
    /// top row + right column, at <see cref="DataMatrixSignatureDecoder.ModulePx"/> pitch.</summary>
    public void DrawDataMatrixLFinder(int left, int top, int modules)
    {
        var sizePx = modules * DataMatrixSignatureDecoder.ModulePx;
        var right = left + sizePx - 1;
        var bottom = top + sizePx - 1;

        // Clock track first, solid border last: the border's own corner pixels (top-left,
        // bottom-right) coincide with the clock track's first/last module cells, so whichever is
        // drawn last wins there - the decoder's alternating-row/column checks already skip those
        // shared corners (see DataMatrixSignatureDecoder's remarks), so the border reliably
        // staying solid at its own corners is what matters here.
        for (var m = 0; m < modules; m++)
        {
            var ink = m % 2 == 0;
            FillRect(left + m * DataMatrixSignatureDecoder.ModulePx, top, DataMatrixSignatureDecoder.ModulePx, DataMatrixSignatureDecoder.ModulePx, ink); // top row
            FillRect(right - DataMatrixSignatureDecoder.ModulePx + 1, top + m * DataMatrixSignatureDecoder.ModulePx, DataMatrixSignatureDecoder.ModulePx, DataMatrixSignatureDecoder.ModulePx, ink); // right column
        }

        FillRect(left, top, DataMatrixSignatureDecoder.ModulePx, sizePx, ink: true); // solid left column
        FillRect(left, bottom - DataMatrixSignatureDecoder.ModulePx + 1, sizePx, DataMatrixSignatureDecoder.ModulePx, ink: true); // solid bottom row
    }

    /// <summary>Renders an Aztec-style bullseye: concentric alternating square rings centered at
    /// (<paramref name="centerX"/>, <paramref name="centerY"/>).</summary>
    public void DrawAztecBullseye(int centerX, int centerY, int rings)
    {
        for (var r = rings - 1; r >= 0; r--)
        {
            var ink = r % 2 == 0;
            var half = r * AztecSignatureDecoder.ModulePx;
            FillRect(centerX - half, centerY - half, half * 2 + 1, half * 2 + 1, ink);
        }
    }

    /// <summary>Renders several stacked dense rows - the simplified PDF417 signature
    /// <see cref="Pdf417SignatureDecoder"/> looks for.</summary>
    public void DrawStackedRows(int rowCount, int left, int top, int rowWidthPx, int rowHeightPx, int rowGapPx)
    {
        var moduleWidths = new[] { 1, 2, 1, 3, 1, 2, 1, 4, 1, 2, 1, 3, 1 };
        for (var r = 0; r < rowCount; r++)
        {
            var y = top + r * (rowHeightPx + rowGapPx);
            var x = left;
            foreach (var w in moduleWidths)
            {
                var widthPx = w * Code128SignatureDecoder.ModulePx;
                if (x + widthPx - left > rowWidthPx)
                {
                    break;
                }

                FillRect(x, y, widthPx, rowHeightPx, ink: true);
                x += widthPx * 2; // leave a matching-width gap after each bar
            }
        }
    }

    public BarcodeRaster ToRaster() => BarcodeRaster.Create(Width, Height, _ink);
}

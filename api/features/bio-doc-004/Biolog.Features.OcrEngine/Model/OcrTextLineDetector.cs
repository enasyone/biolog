namespace Biolog.Features.OcrEngine.Model;

/// <summary>One detected text line: a row band whose column-ink projection resolved into evenly
/// spaced, fixed-pitch glyph cells, and each cell's left pixel column.</summary>
public sealed record DetectedTextLine(int Top, IReadOnlyList<int> CellStartColumns);

/// <summary>
/// Locates arbitrary text lines anywhere on a page (BIO-DOC-004-AC02: generic OCR has no template
/// to tell it where to look). This is BIO-DOC-002's <c>MrzRegionDetector</c> row/column
/// ink-projection algorithm generalized: MRZ only ever accepted a line matching one of three
/// registered lengths (30/36/44) as part of a 2-3-line block; generic text has no such
/// constraint, so any well-formed line of one or more fixed-pitch glyph cells - however many
/// characters, wherever on the page, standing entirely alone - is a valid detection.
///
/// <para>
/// Cell-grid detection uses the same grid-HYPOTHESIS-verification approach BIO-DOC-002 settled
/// on after finding a contiguous-ink-run approach fragments on glyphs with blank edge/internal
/// columns (this font has several: <c>'1'</c>, <c>'I'</c>, punctuation marks): every ink column in
/// a candidate row band must fall inside some cell's window at the known fixed pitch, independent
/// of whether any single glyph's own ink is contiguous.
/// </para>
/// </summary>
public static class OcrTextLineDetector
{
    public static IReadOnlyList<DetectedTextLine> DetectLines(DocumentRaster raster)
    {
        var lines = new List<DetectedTextLine>();
        foreach (var (top, height) in FindRowBands(raster))
        {
            if (height != OcrGlyphFont.GlyphHeight)
            {
                continue;
            }

            var cells = FindColumnCells(raster, top, height);
            if (cells.Count > 0)
            {
                lines.Add(new DetectedTextLine(top, cells));
            }
        }

        return lines;
    }

    private static List<(int Top, int Height)> FindRowBands(DocumentRaster raster)
    {
        var bands = new List<(int Top, int Height)>();
        var inBand = false;
        var bandStart = 0;

        for (var y = 0; y < raster.Height; y++)
        {
            var hasInk = RowHasInk(raster, y);
            if (hasInk && !inBand)
            {
                inBand = true;
                bandStart = y;
            }
            else if (!hasInk && inBand)
            {
                inBand = false;
                bands.Add((bandStart, y - bandStart));
            }
        }

        if (inBand)
        {
            bands.Add((bandStart, raster.Height - bandStart));
        }

        return bands;
    }

    private static bool RowHasInk(DocumentRaster raster, int y)
    {
        for (var x = 0; x < raster.Width; x++)
        {
            if (raster.HasInkAt(x, y))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ColumnHasInk(DocumentRaster raster, int x, int top, int height)
    {
        for (var y = top; y < top + height; y++)
        {
            if (raster.HasInkAt(x, y))
            {
                return true;
            }
        }

        return false;
    }

    private static List<int> FindColumnCells(DocumentRaster raster, int top, int height)
    {
        var inkColumns = new List<int>();
        for (var x = 0; x < raster.Width; x++)
        {
            if (ColumnHasInk(raster, x, top, height))
            {
                inkColumns.Add(x);
            }
        }

        if (inkColumns.Count == 0)
        {
            return [];
        }

        var leftmostInk = inkColumns[0];
        var rightmostInk = inkColumns[^1];
        var earliestLeftStart = Math.Max(0, leftmostInk - OcrGlyphFont.GlyphWidth + 1);
        List<int>? found = null;

        for (var leftStart = earliestLeftStart; leftStart <= leftmostInk; leftStart++)
        {
            var maxLineLength = (raster.Width - leftStart + 1) / OcrGlyphFont.CellPitchX;
            for (var lineLength = 1; lineLength <= maxLineLength; lineLength++)
            {
                // The candidate's LAST cell must be the one actually containing the rightmost ink
                // column - a shorter length would exclude it, a longer one would leave the last
                // cell(s) trivially blank and "fit" by vacuous truth. Pinning this down means at
                // most one lineLength is ever a candidate per leftStart.
                var lastCellStart = leftStart + (lineLength - 1) * OcrGlyphFont.CellPitchX;
                if (rightmostInk < lastCellStart || rightmostInk >= lastCellStart + OcrGlyphFont.GlyphWidth)
                {
                    continue;
                }

                if (!inkColumns.All(x => FitsGrid(x, leftStart, lineLength)))
                {
                    continue;
                }

                if (found is not null)
                {
                    return []; // a second independently-valid grid hypothesis - ambiguous, never guess.
                }

                found = Enumerable.Range(0, lineLength).Select(i => leftStart + i * OcrGlyphFont.CellPitchX).ToList();
            }
        }

        return found ?? [];
    }

    private static bool FitsGrid(int x, int leftStart, int lineLength)
    {
        var offset = x - leftStart;
        if (offset < 0)
        {
            return false;
        }

        var cellIndex = offset / OcrGlyphFont.CellPitchX;
        var withinCell = offset % OcrGlyphFont.CellPitchX;
        return cellIndex < lineLength && withinCell < OcrGlyphFont.GlyphWidth;
    }
}

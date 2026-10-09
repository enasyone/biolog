namespace Biolog.Features.MrzEngine.Model;

/// <summary>One detected MRZ text line within a <see cref="DocumentRaster"/>: a row band whose
/// column-ink projection resolved into evenly spaced, fixed-pitch glyph cells.</summary>
public sealed record MrzDetectedLine(int Top, int LineLength, IReadOnlyList<int> CellStartColumns);

/// <summary>The outcome of <see cref="MrzRegionDetector.Detect"/> (BIO-DOC-002-AC01).</summary>
public sealed record MrzDetectionResult(IReadOnlyList<MrzDetectedLine> Lines, string? RejectionReason)
{
    public bool Found => RejectionReason is null;

    public static MrzDetectionResult NotFound(string reason) => new([], reason);

    public static MrzDetectionResult Ok(IReadOnlyList<MrzDetectedLine> lines) => new(lines, null);
}

/// <summary>
/// Locates the MRZ block within a <see cref="DocumentRaster"/> (BIO-DOC-002-AC01) using row/column
/// ink-projection profiling - a standard, dependency-free document-layout-analysis technique (no
/// CV/ML library needed): rows are grouped into horizontal text-line bands by blank-row gaps,
/// bands are kept only if their height exactly matches <see cref="MrzGlyphFont.GlyphHeight"/> (a
/// non-text region like a photo block will not, by construction, share that exact height in the
/// synthetic fixtures this engine is tested against), each surviving band's column-ink projection
/// is grouped into candidate glyph cells and accepted only if the cell count is one of the
/// registered MRZ line lengths (30/36/44) at the expected fixed pitch, and finally a run of
/// vertically-consecutive, equal-length candidate lines (2 or 3, matching a registered format's
/// line count) is required - a single stray matching line is not enough to call it an MRZ block.
/// </summary>
public static class MrzRegionDetector
{
    public static MrzDetectionResult Detect(DocumentRaster raster)
    {
        var bands = FindRowBands(raster);
        var candidateLines = new List<MrzDetectedLine>();
        foreach (var (top, height) in bands)
        {
            if (height != MrzGlyphFont.GlyphHeight)
            {
                continue;
            }

            var cells = FindColumnCells(raster, top, height);
            if (cells.Count == 0)
            {
                continue;
            }

            candidateLines.Add(new MrzDetectedLine(top, cells.Count, cells));
        }

        if (candidateLines.Count == 0)
        {
            return MrzDetectionResult.NotFound("mrz_region_not_detected");
        }

        var runs = GroupIntoConsecutiveRuns(candidateLines);
        var qualifyingRuns = runs
            .Where(run => run.Count is 2 or 3 && run.All(l => l.LineLength == run[0].LineLength))
            .Where(run => MrzFormatRegistry.ResolveByShape(run.Count, run[0].LineLength).Count > 0)
            .ToList();

        return qualifyingRuns.Count switch
        {
            0 => MrzDetectionResult.NotFound("mrz_region_not_detected"),
            1 => MrzDetectionResult.Ok(qualifyingRuns[0]),
            _ => MrzDetectionResult.NotFound("multiple_mrz_candidates"),
        };
    }

    private static List<(int Top, int Height)> FindRowBands(DocumentRaster raster)
    {
        var bands = new List<(int Top, int Height)>();
        var inBand = false;
        var bandStart = 0;

        for (var y = 0; y < raster.Height; y++)
        {
            var hasInk = raster.RowHasInk(y);
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

    /// <summary>
    /// Finds this band's glyph cell grid by HYPOTHESIS VERIFICATION rather than by inferring cell
    /// boundaries from contiguous ink runs. A run-based approach assumes every glyph fills its
    /// entire 5-wide cell with one unbroken ink blob - false for several real characters in this
    /// engine's own font (e.g. '1' and 'I' both have a fully blank leading column, 'J' has a
    /// blank internal column), so a run-based scan would fragment or misplace their cells. This
    /// instead: (1) reads every ink-bearing column in the band, (2) for each registered line
    /// length and each of the up to <see cref="MrzGlyphFont.GlyphWidth"/> plausible left edges
    /// consistent with the leftmost ink column, checks whether EVERY ink column in the band falls
    /// inside some cell's 5-wide window at that hypothesized pitch (never in a gap, never past
    /// the last cell) - independent of whether any single glyph's own ink is contiguous. Exactly
    /// one (line length, left edge) hypothesis surviving is accepted; zero or more than one is not
    /// a well-formed candidate line at all (returns empty - AC01/AC05's "never guess").
    /// </summary>
    private static List<int> FindColumnCells(DocumentRaster raster, int top, int height)
    {
        var inkColumns = new List<int>();
        for (var x = 0; x < raster.Width; x++)
        {
            if (raster.ColumnHasInk(x, top, height))
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
        List<int>? found = null;

        foreach (var lineLength in MrzFormatRegistry.All.Select(f => f.LineLength).Distinct())
        {
            var blockWidth = lineLength * MrzGlyphFont.CellPitchX - 1;
            var earliestLeftStart = Math.Max(0, leftmostInk - MrzGlyphFont.GlyphWidth + 1);

            for (var leftStart = earliestLeftStart; leftStart <= leftmostInk; leftStart++)
            {
                if (leftStart + blockWidth > raster.Width || rightmostInk >= leftStart + blockWidth)
                {
                    continue;
                }

                if (!inkColumns.All(x => FitsGrid(x, leftStart, lineLength)))
                {
                    continue;
                }

                if (found is not null)
                {
                    return []; // a second independently-valid grid hypothesis - ambiguous, refuse both.
                }

                found = Enumerable.Range(0, lineLength).Select(i => leftStart + i * MrzGlyphFont.CellPitchX).ToList();
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

        var cellIndex = offset / MrzGlyphFont.CellPitchX;
        var withinCell = offset % MrzGlyphFont.CellPitchX;
        return cellIndex < lineLength && withinCell < MrzGlyphFont.GlyphWidth;
    }

    private static List<List<MrzDetectedLine>> GroupIntoConsecutiveRuns(List<MrzDetectedLine> lines)
    {
        var ordered = lines.OrderBy(l => l.Top).ToList();
        var runs = new List<List<MrzDetectedLine>>();
        var current = new List<MrzDetectedLine> { ordered[0] };

        for (var i = 1; i < ordered.Count; i++)
        {
            var previous = ordered[i - 1];
            var line = ordered[i];
            var isAdjacent = line.Top == previous.Top + MrzGlyphFont.GlyphHeight + 1;

            if (isAdjacent)
            {
                current.Add(line);
            }
            else
            {
                runs.Add(current);
                current = [line];
            }
        }

        runs.Add(current);
        return runs;
    }
}

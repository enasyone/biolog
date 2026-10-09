namespace Biolog.Features.BarcodeEngine.Model;

/// <summary>Shared row-band and column-run scanning helpers every decoder in this project builds
/// its own symbology-specific detection on top of - the 1D equivalent of BIO-DOC-002's
/// <c>MrzRegionDetector</c> row/column ink-projection profiling.</summary>
internal static class RasterScanning
{
    public static IReadOnlyList<(int Top, int Height)> FindRowBands(BarcodeRaster raster)
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

    public static bool RowHasInk(BarcodeRaster raster, int y)
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

    public static bool ColumnHasInk(BarcodeRaster raster, int x, int top, int height)
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

    /// <summary>The smallest rectangle containing every ink pixel in the raster, or null if the
    /// raster is entirely blank. Detection-only decoders use this as their one candidate region -
    /// each test raster in this suite renders a single symbol, so the overall ink extent already
    /// is that symbol's region.</summary>
    public static (int Left, int Top, int Right, int Bottom)? FindOverallInkBounds(BarcodeRaster raster)
    {
        var left = int.MaxValue;
        var top = int.MaxValue;
        var right = int.MinValue;
        var bottom = int.MinValue;

        for (var y = 0; y < raster.Height; y++)
        {
            for (var x = 0; x < raster.Width; x++)
            {
                if (!raster.HasInkAt(x, y))
                {
                    continue;
                }

                if (x < left) left = x;
                if (x > right) right = x;
                if (y < top) top = y;
                if (y > bottom) bottom = y;
            }
        }

        return right >= left ? (left, top, right, bottom) : null;
    }

    /// <summary>The ordered sequence of ink/no-ink column runs across a row band's full width -
    /// exact for a uniform-height vertical-bar symbol (1D barcodes), and safely inert (simply
    /// won't match any known pattern) for anything else.</summary>
    public static List<(bool Ink, int Width)> FindColumnRuns(BarcodeRaster raster, int top, int height)
    {
        var runs = new List<(bool Ink, int Width)>();
        var currentInk = false;
        var runStart = 0;
        var started = false;

        for (var x = 0; x <= raster.Width; x++)
        {
            var hasInk = x < raster.Width && ColumnHasInk(raster, x, top, height);
            if (!started)
            {
                started = true;
                currentInk = hasInk;
                runStart = x;
                continue;
            }

            if (hasInk != currentInk)
            {
                runs.Add((currentInk, x - runStart));
                currentInk = hasInk;
                runStart = x;
            }
        }

        return runs;
    }
}

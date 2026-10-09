using Biolog.Features.DocumentVerify.Model;

namespace Biolog.Features.BarcodeEngine.Model;

/// <summary>
/// Data Matrix detection via its real "L" finder pattern (BIO-DOC-003-AC01): a solid ink border
/// along the left column and bottom row, with an alternating "clock track" along the top row and
/// right column. Detection only, not payload decode - see <see cref="QrSignatureDecoder"/>'s
/// remarks for why (identical reasoning, different symbology).
/// </summary>
public sealed class DataMatrixSignatureDecoder : IBarcodeDecoder
{
    public const int ModulePx = 4;

    public BarcodeSymbology Symbology => BarcodeSymbology.DataMatrix;

    public BarcodeDecodeAttempt TryDecode(BarcodeRaster raster)
    {
        var bounds = RasterScanning.FindOverallInkBounds(raster);
        if (bounds is null)
        {
            return BarcodeDecodeAttempt.NotDetected(Symbology);
        }

        var (left, top, right, bottom) = bounds.Value;

        if (!IsSolidColumn(raster, left, top, bottom) || !IsSolidRow(raster, bottom, left, right))
        {
            return BarcodeDecodeAttempt.NotDetected(Symbology);
        }

        if (!IsAlternatingRow(raster, top, left, right) || !IsAlternatingColumn(raster, right, top, bottom))
        {
            return BarcodeDecodeAttempt.NotDetected(Symbology);
        }

        var boundingBox = BoundingBox.Create(
            (double)left / raster.Width,
            (double)top / raster.Height,
            (double)(right - left + 1) / raster.Width,
            (double)(bottom - top + 1) / raster.Height);

        return BarcodeDecodeAttempt.DetectedButNotDecoded(
            Symbology, boundingBox, confidence: 1.0,
            reason: "data_matrix_payload_decode_not_implemented_in_this_story");
    }

    private static bool IsSolidColumn(BarcodeRaster raster, int x, int top, int bottom)
    {
        for (var y = top; y <= bottom; y++)
        {
            if (!raster.HasInkAt(x, y))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsSolidRow(BarcodeRaster raster, int y, int left, int right)
    {
        for (var x = left; x <= right; x++)
        {
            if (!raster.HasInkAt(x, y))
            {
                return false;
            }
        }

        return true;
    }

    // Samples strictly between the two ends, not at them: the row/column's first and last module
    // positions coincide with the solid-border's own corner pixels (top-left shares a pixel with
    // the solid left column; bottom-right shares one with the solid bottom row), so what the
    // solid border requires there and what a strict alternation would require can genuinely
    // conflict. Skipping both ends avoids that corner ambiguity rather than resolving it via draw
    // order - a small, documented simplification of the real clock track's geometry, not a
    // production-grade Data Matrix reader.
    private static bool IsAlternatingRow(BarcodeRaster raster, int y, int left, int right)
    {
        bool? previous = null;
        for (var x = left + ModulePx; x <= right - ModulePx; x += ModulePx)
        {
            var ink = raster.HasInkAt(x, y);
            if (previous == ink)
            {
                return false;
            }
            previous = ink;
        }

        return previous is not null;
    }

    private static bool IsAlternatingColumn(BarcodeRaster raster, int x, int top, int bottom)
    {
        bool? previous = null;
        for (var y = top + ModulePx; y <= bottom - ModulePx; y += ModulePx)
        {
            var ink = raster.HasInkAt(x, y);
            if (previous == ink)
            {
                return false;
            }
            previous = ink;
        }

        return previous is not null;
    }
}

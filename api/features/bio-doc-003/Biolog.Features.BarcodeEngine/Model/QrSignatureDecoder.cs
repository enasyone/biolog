using Biolog.Features.DocumentVerify.Model;

namespace Biolog.Features.BarcodeEngine.Model;

/// <summary>
/// QR detection via its real ISO/IEC 18004 finder pattern (BIO-DOC-003-AC01): the nested
/// square-in-square-in-square module pattern present at three of a QR symbol's four corners.
/// Three of this pattern occurring together in one raster is a highly distinctive, low-
/// false-positive QR signature. This story detects a QR symbol's presence and region; it does
/// not implement the Reed-Solomon-corrected byte-mode payload decode that would be needed to
/// extract QR's actual data (BIO-DOC-003-AC01's "replaceable decoder interfaces" - a future
/// decoder can register full QR decode behind the same <see cref="IBarcodeDecoder"/> seam without
/// this detection logic changing).
/// </summary>
public sealed class QrSignatureDecoder : IBarcodeDecoder
{
    public const int ModulePx = 3;

    // The real QR finder pattern: a solid 7x7 outer ring, a 5x5 white ring, a solid 3x3 core.
    private static readonly bool[,] FinderPattern = BuildFinderPattern();

    public BarcodeSymbology Symbology => BarcodeSymbology.Qr;

    public BarcodeDecodeAttempt TryDecode(BarcodeRaster raster)
    {
        var patternSizePx = FinderPattern.GetLength(0) * ModulePx;
        var matches = new List<(int Left, int Top)>();

        for (var top = 0; top + patternSizePx <= raster.Height; top++)
        {
            for (var left = 0; left + patternSizePx <= raster.Width; left++)
            {
                if (MatchesFinderPattern(raster, left, top))
                {
                    matches.Add((left, top));
                }
            }
        }

        var distinctMatches = DeduplicateOverlapping(matches, patternSizePx);
        if (distinctMatches.Count < 3)
        {
            return BarcodeDecodeAttempt.NotDetected(Symbology);
        }

        var bounds = RasterScanning.FindOverallInkBounds(raster);
        if (bounds is null)
        {
            return BarcodeDecodeAttempt.NotDetected(Symbology);
        }

        var (inkLeft, inkTop, right, bottom) = bounds.Value;
        var boundingBox = BoundingBox.Create(
            (double)inkLeft / raster.Width,
            (double)inkTop / raster.Height,
            (double)(right - inkLeft + 1) / raster.Width,
            (double)(bottom - inkTop + 1) / raster.Height);

        return BarcodeDecodeAttempt.DetectedButNotDecoded(
            Symbology, boundingBox, confidence: 1.0,
            reason: "qr_payload_decode_not_implemented_in_this_story");
    }

    private static bool MatchesFinderPattern(BarcodeRaster raster, int left, int top)
    {
        var modules = FinderPattern.GetLength(0);
        for (var my = 0; my < modules; my++)
        {
            for (var mx = 0; mx < modules; mx++)
            {
                var expectedInk = FinderPattern[my, mx];
                for (var py = 0; py < ModulePx; py++)
                {
                    for (var px = 0; px < ModulePx; px++)
                    {
                        if (raster.HasInkAt(left + mx * ModulePx + px, top + my * ModulePx + py) != expectedInk)
                        {
                            return false;
                        }
                    }
                }
            }
        }

        return true;
    }

    private static List<(int Left, int Top)> DeduplicateOverlapping(List<(int Left, int Top)> matches, int patternSizePx)
    {
        var kept = new List<(int Left, int Top)>();
        foreach (var candidate in matches)
        {
            if (kept.All(k => Math.Abs(k.Left - candidate.Left) >= patternSizePx || Math.Abs(k.Top - candidate.Top) >= patternSizePx))
            {
                kept.Add(candidate);
            }
        }

        return kept;
    }

    private static bool[,] BuildFinderPattern()
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

        var pattern = new bool[7, 7];
        for (var y = 0; y < 7; y++)
        {
            for (var x = 0; x < 7; x++)
            {
                pattern[y, x] = rows[y][x] == '1';
            }
        }

        return pattern;
    }
}

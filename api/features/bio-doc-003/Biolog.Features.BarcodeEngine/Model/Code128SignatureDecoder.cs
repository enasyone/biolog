using Biolog.Features.DocumentVerify.Model;

namespace Biolog.Features.BarcodeEngine.Model;

/// <summary>
/// Code 128 detection via its real structural distinction from Code 39 (BIO-DOC-003-AC01): Code
/// 128 bar/space elements take four distinct module widths (1-4 modules), where Code 39 uses
/// exactly two (narrow/wide) - a genuine, checkable difference between the two symbologies'
/// encoding schemes, usable without needing Code 128's full 103-symbol character table (a real
/// transcription-accuracy risk with no reference to check against in this sandboxed environment;
/// the same honesty applied to <see cref="Code39Table"/>). Detection only, not payload decode.
/// </summary>
public sealed class Code128SignatureDecoder : IBarcodeDecoder
{
    public const int ModulePx = 4;
    public const int MinRuns = 10;
    public const int MinDistinctModuleWidths = 3;

    public BarcodeSymbology Symbology => BarcodeSymbology.Code128;

    public BarcodeDecodeAttempt TryDecode(BarcodeRaster raster)
    {
        foreach (var (top, height) in RasterScanning.FindRowBands(raster))
        {
            var runs = RasterScanning.FindColumnRuns(raster, top, height);
            var firstInk = runs.FindIndex(r => r.Ink);
            var lastInk = runs.FindLastIndex(r => r.Ink);
            if (firstInk < 0)
            {
                continue;
            }

            // Only the runs between the first and last ink column are the symbol itself - the
            // raster's arbitrary left/right margin is not module-pitch-aligned and must not be
            // mistaken for a third or fourth "module width" in the signal.
            var symbolRuns = runs.GetRange(firstInk, lastInk - firstInk + 1);
            if (symbolRuns.Count < MinRuns)
            {
                continue;
            }

            var distinctModuleWidths = symbolRuns
                .Select(r => (int)Math.Round((double)r.Width / ModulePx, MidpointRounding.AwayFromZero))
                .Where(m => m >= 1)
                .Distinct()
                .Count();

            if (distinctModuleWidths < MinDistinctModuleWidths)
            {
                continue;
            }

            var starts = new int[runs.Count + 1];
            for (var i = 0; i < runs.Count; i++)
            {
                starts[i + 1] = starts[i] + runs[i].Width;
            }

            var left = starts[firstInk];
            var right = starts[lastInk + 1];

            var boundingBox = BoundingBox.Create(
                (double)left / raster.Width,
                (double)top / raster.Height,
                (double)(right - left) / raster.Width,
                (double)height / raster.Height);

            return BarcodeDecodeAttempt.DetectedButNotDecoded(
                Symbology, boundingBox, confidence: 1.0,
                reason: "code128_payload_decode_not_implemented_in_this_story");
        }

        return BarcodeDecodeAttempt.NotDetected(Symbology);
    }
}

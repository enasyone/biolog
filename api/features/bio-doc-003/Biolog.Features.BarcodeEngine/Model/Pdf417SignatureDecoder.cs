using Biolog.Features.DocumentVerify.Model;

namespace Biolog.Features.BarcodeEngine.Model;

/// <summary>
/// PDF417 detection via a simplified native approximation of its real structure (BIO-DOC-003-AC01):
/// PDF417 is genuinely a stack of multiple linear rows, each densely packed with narrow bar/space
/// codewords. This checks for several (<see cref="MinStackedRows"/>+) row bands, each with many
/// column transitions - it does not reproduce ISO/IEC 15438's exact start/stop guard-bar module
/// widths from memory (a real transcription-accuracy risk with no reference to check against in
/// this sandboxed environment; the same honesty applied to <see cref="Code39Table"/>). Detection
/// only, not payload decode.
/// </summary>
public sealed class Pdf417SignatureDecoder : IBarcodeDecoder
{
    public const int MinStackedRows = 3;
    public const int MinRunsPerRow = 8;

    public BarcodeSymbology Symbology => BarcodeSymbology.Pdf417;

    public BarcodeDecodeAttempt TryDecode(BarcodeRaster raster)
    {
        var bands = RasterScanning.FindRowBands(raster);
        var denseBands = bands.Where(b => RasterScanning.FindColumnRuns(raster, b.Top, b.Height).Count >= MinRunsPerRow).ToList();

        if (denseBands.Count < MinStackedRows)
        {
            return BarcodeDecodeAttempt.NotDetected(Symbology);
        }

        var bounds = RasterScanning.FindOverallInkBounds(raster);
        if (bounds is null)
        {
            return BarcodeDecodeAttempt.NotDetected(Symbology);
        }

        var (left, top, right, bottom) = bounds.Value;
        var boundingBox = BoundingBox.Create(
            (double)left / raster.Width,
            (double)top / raster.Height,
            (double)(right - left + 1) / raster.Width,
            (double)(bottom - top + 1) / raster.Height);

        return BarcodeDecodeAttempt.DetectedButNotDecoded(
            Symbology, boundingBox, confidence: 1.0,
            reason: "pdf417_payload_decode_not_implemented_in_this_story");
    }
}

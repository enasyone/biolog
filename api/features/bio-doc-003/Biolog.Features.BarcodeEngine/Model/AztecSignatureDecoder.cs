using Biolog.Features.DocumentVerify.Model;

namespace Biolog.Features.BarcodeEngine.Model;

/// <summary>
/// Aztec detection via its real central "bullseye" finder pattern (BIO-DOC-003-AC01): concentric
/// square rings of alternating ink centered on the symbol. This engine samples a fixed number of
/// rings outward from the ink region's center rather than reproducing ISO/IEC 24778's exact
/// compact/full-symbol ring counts - a simplified native approximation of the real structural
/// signature, not a byte-for-byte reproduction (documented explicitly, same scope choice as
/// <see cref="Code39Table"/>). Detection only, not payload decode.
/// </summary>
public sealed class AztecSignatureDecoder : IBarcodeDecoder
{
    public const int ModulePx = 4;
    public const int MinAlternatingRings = 4;

    public BarcodeSymbology Symbology => BarcodeSymbology.Aztec;

    public BarcodeDecodeAttempt TryDecode(BarcodeRaster raster)
    {
        var bounds = RasterScanning.FindOverallInkBounds(raster);
        if (bounds is null)
        {
            return BarcodeDecodeAttempt.NotDetected(Symbology);
        }

        var (left, top, right, bottom) = bounds.Value;
        var centerX = (left + right) / 2;
        var centerY = (top + bottom) / 2;

        if (!HasAlternatingBullseye(raster, centerX, centerY))
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
            reason: "aztec_payload_decode_not_implemented_in_this_story");
    }

    private static bool HasAlternatingBullseye(BarcodeRaster raster, int centerX, int centerY)
    {
        bool? previous = null;
        for (var ring = 0; ring < MinAlternatingRings; ring++)
        {
            var ink = raster.HasInkAt(centerX + ring * ModulePx, centerY);
            if (previous == ink)
            {
                return false;
            }
            previous = ink;
        }

        return true;
    }
}

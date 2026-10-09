namespace Biolog.Features.BarcodeEngine.Model;

/// <summary>The result of scanning one raster with every registered decoder
/// (BIO-DOC-003-AC01/AC02). <see cref="Decoded"/> holds a full evidence-safe result per
/// successfully decoded symbol; <see cref="UndecodedDetections"/> holds symbols a decoder found
/// but could not (or, for this story's five detection-only decoders, does not yet) extract a
/// payload from - reported explicitly rather than silently dropped.</summary>
public sealed record BarcodeScanOutcome(
    IReadOnlyList<BarcodeDecodeResult> Decoded,
    IReadOnlyList<BarcodeDecodeAttempt> UndecodedDetections);

/// <summary>
/// Runs every registered <see cref="IBarcodeDecoder"/> against a raster (BIO-DOC-003-AC01: one
/// service, many replaceable decoders). This is BIOLOG's own owned orchestration layer - the
/// master specification's framing that "BIOLOG owns parsers, validation and document
/// intelligence" while low-level decoding itself is replaceable.
/// </summary>
public sealed class BarcodeDetectionService
{
    private readonly BarcodeDecoderRegistry _registry;

    public BarcodeDetectionService(BarcodeDecoderRegistry registry)
    {
        _registry = registry;
    }

    public BarcodeScanOutcome Scan(BarcodeRaster raster)
    {
        var decoded = new List<BarcodeDecodeResult>();
        var undecodedDetections = new List<BarcodeDecodeAttempt>();

        foreach (var decoder in _registry.All)
        {
            var attempt = TryDecodeSafely(decoder, raster);

            if (attempt.Decoded)
            {
                decoded.Add(BarcodeDecodeResult.FromDecodedAttempt(attempt));
            }
            else if (attempt.Detected)
            {
                undecodedDetections.Add(attempt);
            }
        }

        return new BarcodeScanOutcome(decoded, undecodedDetections);
    }

    // BIO-DOC-003-AC03: a raster can carry hostile/malformed content by construction - this is a
    // trust boundary (untrusted document input), not a scenario "that can't happen", so a defensive
    // per-decoder isolation boundary is warranted here even though every decoder is also written
    // defensively in its own right. One misbehaving decoder must never abort the whole scan or
    // any other decoder's result.
    private static BarcodeDecodeAttempt TryDecodeSafely(IBarcodeDecoder decoder, BarcodeRaster raster)
    {
        try
        {
            return decoder.TryDecode(raster);
        }
        catch (Exception)
        {
            return BarcodeDecodeAttempt.NotDetected(decoder.Symbology);
        }
    }
}

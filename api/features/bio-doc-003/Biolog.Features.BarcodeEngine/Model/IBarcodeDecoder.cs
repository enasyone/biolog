namespace Biolog.Features.BarcodeEngine.Model;

/// <summary>
/// The "replaceable decoder interface" BIO-DOC-003-AC01 requires: one implementation per
/// symbology, registered into <see cref="BarcodeDecoderRegistry"/> and swappable without
/// touching <see cref="BarcodeDetectionService"/> or anything above it. A future story can
/// register a higher-fidelity or platform-native decoder for any symbology (the master
/// specification's own framing: "Barcode: low-level decoding via replaceable interfaces; BIOLOG
/// owns parsers, validation and document intelligence") by implementing this interface again -
/// nothing else in this feature needs to change.
/// </summary>
public interface IBarcodeDecoder
{
    BarcodeSymbology Symbology { get; }

    /// <summary>Attempts to find and decode this decoder's symbology within
    /// <paramref name="raster"/>. Must never throw on malformed/hostile raster content
    /// (BIO-DOC-003-AC03) - an implementation that cannot make sense of what it finds returns
    /// <see cref="BarcodeDecodeAttempt.NotDetected"/> or
    /// <see cref="BarcodeDecodeAttempt.DetectedButNotDecoded"/>, never a fabricated payload.</summary>
    BarcodeDecodeAttempt TryDecode(BarcodeRaster raster);
}

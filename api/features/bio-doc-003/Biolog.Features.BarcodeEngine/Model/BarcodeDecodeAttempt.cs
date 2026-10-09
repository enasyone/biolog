using Biolog.Features.DocumentVerify.Model;

namespace Biolog.Features.BarcodeEngine.Model;

/// <summary>
/// One <see cref="IBarcodeDecoder"/>'s raw output for a candidate region (BIO-DOC-003-AC01/AC02).
/// This is the low-level, per-decoder result - it may carry the actual decoded payload bytes,
/// unlike the service-level <see cref="BarcodeDecodeResult"/> (BIO-DOC-003-AC02 deliberately
/// returns a payload HASH, not the payload itself - see that type's remarks). Nothing about this
/// type asserts the payload is trustworthy or the document is genuine (BIO-DOC-003-AC04):
/// <see cref="Decoded"/> only means "a symbol matching this decoder's structural signature was
/// found and its payload bytes were extracted," nothing more.
/// </summary>
public sealed record BarcodeDecodeAttempt
{
    public BarcodeSymbology Symbology { get; internal init; }

    /// <summary>True if a symbol matching this decoder's structural signature (finder/guard
    /// pattern) was found in the raster at all.</summary>
    public bool Detected { get; internal init; }

    /// <summary>True if the detected symbol's payload bytes were extracted. Some registered
    /// decoders in this story detect but do not decode a payload (BIO-DOC-003-AC01's "replaceable
    /// decoder interfaces" - low-level decoding for the harder 2D symbologies is explicitly
    /// scoped to a later, swappable implementation; see the design doc). Never true when
    /// <see cref="Detected"/> is false.</summary>
    public bool Decoded { get; internal init; }

    public BoundingBox? BoundingBox { get; internal init; }

    /// <summary>Raw decoded payload bytes, exactly as read - never interpreted, executed or
    /// parsed by this type (BIO-DOC-003-AC03: hostile/malformed payloads are inert data here).
    /// Null unless <see cref="Decoded"/> is true.</summary>
    public byte[]? RawPayload { get; internal init; }

    /// <summary>Decode confidence in [0, 1]. 0 when nothing was detected.</summary>
    public double Confidence { get; internal init; }

    /// <summary>Set whenever <see cref="Detected"/> is true but <see cref="Decoded"/> is false -
    /// explains why, rather than silently returning an empty/default payload.</summary>
    public string? UndecodedReason { get; internal init; }

    internal BarcodeDecodeAttempt(
        BarcodeSymbology symbology,
        bool detected,
        bool decoded,
        BoundingBox? boundingBox,
        byte[]? rawPayload,
        double confidence,
        string? undecodedReason)
    {
        if (decoded && !detected)
        {
            throw new ArgumentException("cannot be decoded without first being detected", nameof(decoded));
        }
        if (decoded && rawPayload is null)
        {
            throw new ArgumentException("a decoded attempt must carry a raw payload", nameof(rawPayload));
        }
        if (!decoded && rawPayload is not null)
        {
            throw new ArgumentException("an undecoded attempt must not carry a raw payload", nameof(rawPayload));
        }

        Symbology = symbology;
        Detected = detected;
        Decoded = decoded;
        BoundingBox = boundingBox;
        RawPayload = rawPayload;
        Confidence = confidence;
        UndecodedReason = undecodedReason;
    }

    public static BarcodeDecodeAttempt NotDetected(BarcodeSymbology symbology) =>
        new(symbology, detected: false, decoded: false, boundingBox: null, rawPayload: null, confidence: 0.0, undecodedReason: null);

    public static BarcodeDecodeAttempt DetectedButNotDecoded(BarcodeSymbology symbology, BoundingBox boundingBox, double confidence, string reason) =>
        new(symbology, detected: true, decoded: false, boundingBox, rawPayload: null, confidence, undecodedReason: reason);

    public static BarcodeDecodeAttempt Decode(BarcodeSymbology symbology, BoundingBox boundingBox, byte[] rawPayload, double confidence) =>
        new(symbology, detected: true, decoded: true, boundingBox, rawPayload, confidence, undecodedReason: null);
}

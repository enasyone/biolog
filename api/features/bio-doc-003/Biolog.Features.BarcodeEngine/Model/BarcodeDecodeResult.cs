using System.Security.Cryptography;
using Biolog.Features.DocumentVerify.Model;

namespace Biolog.Features.BarcodeEngine.Model;

/// <summary>
/// The service-level result BIO-DOC-003-AC02 asks for: "symbology, bounding box, raw payload
/// hash and decode confidence" - four fields, not five. The raw payload bytes themselves are
/// deliberately NOT part of this type, only a SHA-256 fingerprint of them: a decoded barcode
/// payload can carry identity PII directly (an AAMVA-format PDF417 driver's-license barcode
/// encodes full name, date of birth and address in plain text), so this mirrors BIO-DOC-001's
/// <c>DocumentEvidence</c> - a hash and metadata are the durable evidence-trail record, never the
/// raw sensitive content itself (ADR-0003 Decision 5: "evidence references, not evidence
/// copies"). A caller that legitimately needs the actual payload bytes (a later parsing story)
/// works from the <see cref="BarcodeDecodeAttempt"/> that produced this result, not from this
/// type.
///
/// <para>
/// There is deliberately no "IsAuthentic"/"IsValid" field anywhere on this type
/// (BIO-DOC-003-AC04: "no decoded barcode alone is labelled authentic"). A successful decode
/// proves only that a machine-readable symbol was present and well-formed - it says nothing about
/// whether the document carrying it is genuine. Every property is <c>internal init</c> with an
/// explicit <c>internal</c> constructor for the same reason BIO-DOC-002's <c>MrzFieldRead</c>/
/// <c>MrzReadOutcome</c> are: this is the trust surface a caller reads, so a public <c>init</c>
/// would let a caller fabricate one via <c>with</c> without this type's factory ever running.
/// </para>
/// </summary>
public sealed record BarcodeDecodeResult
{
    public BarcodeSymbology Symbology { get; internal init; }
    public BoundingBox BoundingBox { get; internal init; } = null!;

    /// <summary>Lowercase hex-encoded SHA-256 of the raw decoded payload bytes (64 characters) -
    /// matching <c>DocumentEvidence.Sha256Hash</c>'s own format (BIO-DOC-001-AC02).</summary>
    public string RawPayloadHash { get; internal init; } = null!;

    public double Confidence { get; internal init; }

    internal BarcodeDecodeResult(BarcodeSymbology symbology, BoundingBox boundingBox, string rawPayloadHash, double confidence)
    {
        Symbology = symbology;
        BoundingBox = boundingBox;
        RawPayloadHash = rawPayloadHash;
        Confidence = confidence;
    }

    /// <summary>Builds a result from a successful decode attempt, hashing (and thereby dropping)
    /// the raw payload. Throws if <paramref name="attempt"/> was not actually decoded - there is
    /// no meaningful result to report otherwise (BIO-DOC-003-AC03: an undetected/undecoded symbol
    /// is never silently reported as an empty or zero-confidence "result").</summary>
    public static BarcodeDecodeResult FromDecodedAttempt(BarcodeDecodeAttempt attempt)
    {
        if (!attempt.Decoded || attempt.RawPayload is null || attempt.BoundingBox is null)
        {
            throw new ArgumentException("attempt must be a successfully decoded payload", nameof(attempt));
        }

        var hash = Convert.ToHexStringLower(SHA256.HashData(attempt.RawPayload));
        return new BarcodeDecodeResult(attempt.Symbology, attempt.BoundingBox, hash, attempt.Confidence);
    }
}

using Biolog.Features.DocumentVerify.Model;

namespace Biolog.Features.OcrEngine.Model;

/// <summary>
/// One block of recognized text with no assigned meaning (BIO-DOC-004-AC02: "generic OCR returns
/// text blocks without pretending field semantics"). Deliberately has no <see cref="OcrFieldType"/>,
/// no field name, nothing that looks like an interpretation - only what was actually read and how
/// confident the recognizer was. A block is one detected text line in this engine (multi-line
/// paragraph grouping is out of scope - see the design doc); "without pretending field semantics"
/// is the guarantee this type's shape itself enforces, not merely a runtime behaviour.
///
/// <para>
/// Locked down the same way as every other trust-bearing result type in this codebase's document-
/// intelligence stories (BIO-DOC-002's <c>MrzFieldRead</c>, BIO-DOC-003's <c>BarcodeDecodeResult</c>).
/// </para>
/// </summary>
public sealed record GenericTextBlock
{
    public string RawText { get; internal init; } = null!;
    public BoundingBox BoundingBox { get; internal init; } = null!;
    public double Confidence { get; internal init; }

    internal GenericTextBlock(string rawText, BoundingBox boundingBox, double confidence)
    {
        RawText = rawText;
        BoundingBox = boundingBox;
        Confidence = confidence;
    }
}

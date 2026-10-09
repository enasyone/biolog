using Biolog.Features.DocumentVerify.Model;

namespace Biolog.Features.OcrEngine.Model;

/// <summary>
/// One template field's OCR result (BIO-DOC-004-AC01: field type, raw value, normalized value,
/// confidence and source coordinates - exactly this type's five data properties).
///
/// <para>
/// Every accessor is <c>internal init</c> with an explicit <c>internal</c> constructor, the same
/// reasoning as BIO-DOC-002's <c>MrzFieldRead</c> and BIO-DOC-003's <c>BarcodeDecodeResult</c>:
/// <see cref="Confidence"/> is exactly the field a caller decides whether to trust unattended, so
/// a public <c>init</c>/<c>with</c> would let a caller launder a low-confidence read into one that
/// looks clean without this engine's own recognition ever running.
/// </para>
/// </summary>
public sealed record OcrFieldRead
{
    public string FieldName { get; internal init; } = null!;
    public OcrFieldType FieldType { get; internal init; }
    public string RawValue { get; internal init; } = null!;
    public string NormalizedValue { get; internal init; } = null!;
    public double Confidence { get; internal init; }
    public BoundingBox BoundingBox { get; internal init; } = null!;
    public bool Critical { get; internal init; }

    internal OcrFieldRead(
        string fieldName,
        OcrFieldType fieldType,
        string rawValue,
        string normalizedValue,
        double confidence,
        BoundingBox boundingBox,
        bool critical)
    {
        FieldName = fieldName;
        FieldType = fieldType;
        RawValue = rawValue;
        NormalizedValue = normalizedValue;
        Confidence = confidence;
        BoundingBox = boundingBox;
        Critical = critical;
    }
}

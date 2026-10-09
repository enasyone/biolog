using Biolog.Features.MrzEngine.Model;
using Biolog.Features.ParserRegistry.Model;

namespace Biolog.Features.EvidenceComparison.Model;

/// <summary>
/// One source's contribution to a comparable field (BIO-DOC-008-AC01/AC02). Deliberately NOT
/// locked down the way this story's own computed result types are - unlike
/// <see cref="FieldComparison"/>/<see cref="CrossComparisonResult"/> (which this engine COMPUTES
/// and which a caller must not be able to forge), a <see cref="SourceFieldValue"/> is INPUT the
/// caller supplies, the same shape as BIO-DOC-007's <c>PassiveAuthenticationInputs</c>. Visual/OCR
/// evidence in particular has no adapter at all here - BIO-DOC-004 is not a declared dependency of
/// this story - so a caller constructing a visual-source value MUST be able to call
/// <see cref="Create"/> directly with a public constructor.
///
/// <para>
/// <b>AC02, "normalization never destroys raw evidence":</b> enforced structurally -
/// <see cref="RawValue"/> and <see cref="NormalizedValue"/> are both required, non-empty, and
/// always carried together. There is no code path anywhere in this type or this engine that
/// accepts only a normalized value and discards the raw one.
/// </para>
/// </summary>
public sealed record SourceFieldValue
{
    public EvidenceSourceKind Source { get; }
    public string RawValue { get; }
    public string NormalizedValue { get; }
    public double Confidence { get; }

    private SourceFieldValue(EvidenceSourceKind source, string rawValue, string normalizedValue, double confidence)
    {
        Source = source;
        RawValue = rawValue;
        NormalizedValue = normalizedValue;
        Confidence = confidence;
    }

    public static SourceFieldValue Create(EvidenceSourceKind source, string rawValue, string normalizedValue, double confidence)
    {
        if (string.IsNullOrEmpty(rawValue))
        {
            throw new ArgumentException("raw value must not be empty", nameof(rawValue));
        }

        if (string.IsNullOrEmpty(normalizedValue))
        {
            throw new ArgumentException("normalized value must not be empty - normalization must never discard the raw value without producing a normalized one", nameof(normalizedValue));
        }

        if (confidence is < 0.0 or > 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(confidence), confidence, "confidence must be between 0 and 1");
        }

        return new SourceFieldValue(source, rawValue, normalizedValue, confidence);
    }

    /// <summary>Adapts a real BIO-DOC-002 MRZ field read - the engine's own recognized raw value,
    /// normalized value and confidence carry over unchanged.</summary>
    public static SourceFieldValue FromMrz(MrzFieldRead field) =>
        new(EvidenceSourceKind.Mrz, field.RawValue, field.NormalizedValue, field.Confidence);

    /// <summary>Adapts a real BIO-DOC-005 parsed-barcode field. <see cref="ParserFieldResult"/>
    /// carries no separately-normalized value or confidence of its own (BIO-DOC-005-AC03's own
    /// scope) - honestly reusing <c>RawValue</c> as the normalized value too, and mapping
    /// <c>Valid</c> to a binary confidence, rather than inventing a normalization or a confidence
    /// scale this source never actually computed.</summary>
    public static SourceFieldValue FromParsedBarcodeField(ParserFieldResult field) =>
        new(EvidenceSourceKind.Barcode, field.RawValue, field.RawValue, field.Valid ? 1.0 : 0.0);
}

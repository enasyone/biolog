using Biolog.Features.DocumentVerify.Model;

namespace Biolog.Features.MrzEngine.Model;

/// <summary>
/// One parsed MRZ field (BIO-DOC-002-AC02/AC04): the raw characters exactly as recognized,
/// alongside a normalized display value, the recognizer's confidence, where it sits on the source
/// raster, and - for fields ICAO defines one for - the independently recomputed check digit
/// alongside the one actually read (BIO-DOC-002-AC03). This is a plain, immutable DTO decoupled
/// from any live <c>DocumentSession</c>; <see cref="ToDocumentFieldEvidence"/> is the adapter into
/// BIO-DOC-001's session-scoped evidence model once a caller has one.
///
/// <para>
/// Every accessor is <c>internal init</c>, not the plain public one a positional record would
/// give it. This type has no business-rule "Create" to bypass the way BIO-DOC-001's
/// <c>DocumentEvidence</c> did (QA-03) - there is nothing to validate, every value here is a
/// passive measurement the engine itself computed - but <see cref="CheckDigitValid"/> is exactly
/// the kind of field a caller receiving one of these as evidence needs to actually trust: a
/// public <c>init</c> would let any caller launder a genuinely-failed check
/// (<c>reading with {`CheckDigitValid = true`}</c>) into a record that looks like it came from
/// this engine's own validation. Restricting construction/copy-modification to this assembly
/// closes that off without needing a factory method, since there is no validation for one to run.
/// </para>
/// </summary>
public sealed record MrzFieldRead
{
    public MrzFieldKind Kind { get; internal init; }
    public string FieldName { get; internal init; } = null!;
    public string RawValue { get; internal init; } = null!;
    public string NormalizedValue { get; internal init; } = null!;
    public double Confidence { get; internal init; }
    public BoundingBox BoundingBox { get; internal init; } = null!;
    public bool HasCheckDigit { get; internal init; }
    public char? ReadCheckDigit { get; internal init; }
    public int? ComputedCheckDigit { get; internal init; }
    public bool? CheckDigitValid { get; internal init; }

    internal MrzFieldRead(
        MrzFieldKind kind,
        string fieldName,
        string rawValue,
        string normalizedValue,
        double confidence,
        BoundingBox boundingBox,
        bool hasCheckDigit,
        char? readCheckDigit,
        int? computedCheckDigit,
        bool? checkDigitValid)
    {
        Kind = kind;
        FieldName = fieldName;
        RawValue = rawValue;
        NormalizedValue = normalizedValue;
        Confidence = confidence;
        BoundingBox = boundingBox;
        HasCheckDigit = hasCheckDigit;
        ReadCheckDigit = readCheckDigit;
        ComputedCheckDigit = computedCheckDigit;
        CheckDigitValid = checkDigitValid;
    }

    /// <summary>
    /// Adapts this engine-native read into a BIO-DOC-001 <see cref="DocumentFieldEvidence"/> -
    /// the concrete proof (AC04) that MRZ recognition output round-trips into the shared evidence
    /// model <see cref="FieldEvidenceSource.Mrz"/> already exists for. The caller supplies the
    /// session context this type deliberately does not carry.
    /// </summary>
    public DocumentFieldEvidence ToDocumentFieldEvidence(
        string tenantId,
        string documentSessionId,
        int sequenceNumber,
        int sourceEvidenceSequenceNumber,
        string extractedAt,
        EvidenceClassification classification) =>
        DocumentFieldEvidence.Create(
            tenantId,
            documentSessionId,
            sequenceNumber,
            sourceEvidenceSequenceNumber,
            FieldName,
            RawValue,
            NormalizedValue,
            Confidence,
            FieldEvidenceSource.Mrz,
            BoundingBox,
            extractedAt,
            classification);
}

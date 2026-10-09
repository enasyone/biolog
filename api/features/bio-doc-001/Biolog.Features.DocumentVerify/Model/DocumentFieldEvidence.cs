namespace Biolog.Features.DocumentVerify.Model;

/// <summary>
/// One immutable, append-only extracted-field evidence record (BIO-DOC-001-AC03): what an
/// engine (MRZ/barcode/OCR/NFC) read for one field off one <see cref="DocumentEvidence"/> image.
/// Preserves the raw value exactly as read alongside the normalized value a consumer would
/// actually use, plus provenance (which engine, how confident, where on the image) - so a later
/// dispute or re-processing pass can always see what was actually on the document, not only the
/// system's cleaned-up interpretation of it. Like <see cref="DocumentEvidence"/>, this is a
/// <c>record</c> with no mutator members and no <c>version</c> field (ADR-0001 Decision 3.3):
/// a correction is a new record, never an edit of this one.
/// </summary>
public sealed record DocumentFieldEvidence
{
    // QA-03 (Chioma acceptance audit): same bypass as DocumentEvidence - `private init` on every
    // property is what actually makes `Create` mandatory (see that type's remarks in full).
    public string TenantId { get; private init; } = null!;
    public string DocumentSessionId { get; private init; } = null!;

    /// <summary>1-based, monotonically increasing per session (see <see cref="DocumentEvidence"/>
    /// remarks on identity without a new canonical id type).</summary>
    public int SequenceNumber { get; private init; }

    /// <summary>Which <see cref="DocumentEvidence.SequenceNumber"/> in the same session this
    /// field was read from.</summary>
    public int SourceEvidenceSequenceNumber { get; private init; }

    /// <summary>Namespaced field name, e.g. <c>"given_names"</c>, <c>"document_number"</c>,
    /// <c>"date_of_birth"</c>. Not a closed enum: the registered field vocabulary belongs to the
    /// engines that produce values (BIO-DOC-002+), not this foundation.</summary>
    public string FieldName { get; private init; } = null!;

    /// <summary>Exactly as read off the document - unnormalized, uncorrected.</summary>
    public string RawValue { get; private init; } = null!;

    /// <summary>Canonicalized form (trimmed, cased, date-shaped, etc. per the producing engine's
    /// own rules).</summary>
    public string NormalizedValue { get; private init; } = null!;

    /// <summary>Engine-reported confidence, 0.0 (no confidence) to 1.0 (certain).</summary>
    public double Confidence { get; private init; }

    public FieldEvidenceSource Source { get; private init; }

    /// <summary>Null for a source with no meaningful image location (e.g. an NFC-read field).</summary>
    public BoundingBox? BoundingBox { get; private init; }

    public string ExtractedAt { get; private init; } = null!;

    public EvidenceClassification Classification { get; private init; }

    // See DocumentEvidence's identical private constructor for why this is required, not
    // cosmetic: without it the compiler still synthesizes a public parameterless constructor.
    private DocumentFieldEvidence()
    {
    }

    public static DocumentFieldEvidence Create(
        string tenantId,
        string documentSessionId,
        int sequenceNumber,
        int sourceEvidenceSequenceNumber,
        string fieldName,
        string rawValue,
        string normalizedValue,
        double confidence,
        FieldEvidenceSource source,
        BoundingBox? boundingBox,
        string extractedAt,
        EvidenceClassification classification)
    {
        DocumentEvidence.RequireNonEmpty(tenantId, nameof(tenantId));
        DocumentEvidence.RequireNonEmpty(documentSessionId, nameof(documentSessionId));
        if (sequenceNumber < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(sequenceNumber), sequenceNumber, "must be >= 1");
        }
        if (sourceEvidenceSequenceNumber < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceEvidenceSequenceNumber), sourceEvidenceSequenceNumber, "must be >= 1");
        }
        DocumentEvidence.RequireNonEmpty(fieldName, nameof(fieldName));
        ArgumentNullException.ThrowIfNull(rawValue);
        ArgumentNullException.ThrowIfNull(normalizedValue);
        if (double.IsNaN(confidence) || confidence < 0.0 || confidence > 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(confidence), confidence, "must be in [0.0, 1.0]");
        }
        DocumentEvidence.RequireDefined(source, nameof(source));
        DocumentEvidence.RequireNonEmpty(extractedAt, nameof(extractedAt));
        DocumentEvidence.RequireDefined(classification, nameof(classification));

        return new DocumentFieldEvidence
        {
            TenantId = tenantId,
            DocumentSessionId = documentSessionId,
            SequenceNumber = sequenceNumber,
            SourceEvidenceSequenceNumber = sourceEvidenceSequenceNumber,
            FieldName = fieldName,
            RawValue = rawValue,
            NormalizedValue = normalizedValue,
            Confidence = confidence,
            Source = source,
            BoundingBox = boundingBox,
            ExtractedAt = extractedAt,
            Classification = classification,
        };
    }
}

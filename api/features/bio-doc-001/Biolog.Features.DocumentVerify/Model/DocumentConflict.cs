namespace Biolog.Features.DocumentVerify.Model;

/// <summary>
/// Records that two or more <see cref="DocumentFieldEvidence"/> records for the same field in
/// the same session disagree, and tracks the human review that resolves it (BIO-DOC-001-AC05).
/// Mutable (unlike the evidence records themselves): a conflict has a real lifecycle - raised,
/// picked up, resolved - and ADR-0001 Decision 3's version discipline applies to it the same as
/// any other mutable tenant aggregate.
/// </summary>
public sealed class DocumentConflict
{
    public const int InitialVersion = 1;

    public string TenantId { get; }
    public string DocumentSessionId { get; }
    public string FieldName { get; }

    /// <summary><see cref="DocumentFieldEvidence.SequenceNumber"/> values that disagree.</summary>
    public IReadOnlyList<int> ConflictingFieldEvidenceSequenceNumbers { get; }

    public ManualReviewState ReviewState { get; private set; }
    public int Version { get; private set; }
    public string RaisedAt { get; }

    public string? ResolvedValue { get; private set; }
    public string? ResolvedBy { get; private set; }
    public string? ResolvedAt { get; private set; }

    private DocumentConflict(
        string tenantId,
        string documentSessionId,
        string fieldName,
        IReadOnlyList<int> conflictingSequenceNumbers,
        ManualReviewState reviewState,
        int version,
        string raisedAt)
    {
        TenantId = tenantId;
        DocumentSessionId = documentSessionId;
        FieldName = fieldName;
        ConflictingFieldEvidenceSequenceNumbers = conflictingSequenceNumbers;
        ReviewState = reviewState;
        Version = version;
        RaisedAt = raisedAt;
    }

    /// <summary>Opens a conflict. Requires at least two disagreeing field-evidence sequence
    /// numbers - a "conflict" of one is not a conflict.</summary>
    public static DocumentConflict Raise(
        string tenantId,
        string documentSessionId,
        string fieldName,
        IReadOnlyList<int> conflictingFieldEvidenceSequenceNumbers,
        string raisedAt)
    {
        DocumentEvidence.RequireNonEmpty(tenantId, nameof(tenantId));
        DocumentEvidence.RequireNonEmpty(documentSessionId, nameof(documentSessionId));
        DocumentEvidence.RequireNonEmpty(fieldName, nameof(fieldName));
        DocumentEvidence.RequireNonEmpty(raisedAt, nameof(raisedAt));
        if (conflictingFieldEvidenceSequenceNumbers is null || conflictingFieldEvidenceSequenceNumbers.Count < 2)
        {
            throw new ArgumentException(
                "a conflict requires at least two disagreeing field-evidence sequence numbers",
                nameof(conflictingFieldEvidenceSequenceNumbers));
        }
        if (conflictingFieldEvidenceSequenceNumbers.Distinct().Count() != conflictingFieldEvidenceSequenceNumbers.Count)
        {
            throw new ArgumentException(
                "conflicting field-evidence sequence numbers must be distinct",
                nameof(conflictingFieldEvidenceSequenceNumbers));
        }

        return new DocumentConflict(
            tenantId,
            documentSessionId,
            fieldName,
            conflictingFieldEvidenceSequenceNumbers.ToList(),
            ManualReviewState.PendingReview,
            InitialVersion,
            raisedAt);
    }

    public void BeginReview(int expectedVersion, string nowUtc)
    {
        DocumentEvidence.RequireNonEmpty(nowUtc, nameof(nowUtc));
        if (ReviewState != ManualReviewState.PendingReview)
        {
            throw new InvalidOperationException(
                $"document_conflict_not_pending: conflict on '{FieldName}' is '{ReviewState}'.");
        }
        CheckVersion(expectedVersion);
        ReviewState = ManualReviewState.InReview;
        Bump();
    }

    public void Resolve(string resolvedValue, string resolvedBy, string nowUtc, int expectedVersion)
    {
        ArgumentNullException.ThrowIfNull(resolvedValue);
        DocumentEvidence.RequireNonEmpty(resolvedBy, nameof(resolvedBy));
        DocumentEvidence.RequireNonEmpty(nowUtc, nameof(nowUtc));
        if (ReviewState == ManualReviewState.Resolved)
        {
            throw new InvalidOperationException($"document_conflict_already_resolved: conflict on '{FieldName}'.");
        }
        CheckVersion(expectedVersion);

        ResolvedValue = resolvedValue;
        ResolvedBy = resolvedBy;
        ResolvedAt = nowUtc;
        ReviewState = ManualReviewState.Resolved;
        Bump();
    }

    private void CheckVersion(int expectedVersion)
    {
        if (expectedVersion != Version)
        {
            throw new ConcurrencyException($"{DocumentSessionId}:{FieldName}", expectedVersion, Version);
        }
    }

    private void Bump() => Version += 1;
}

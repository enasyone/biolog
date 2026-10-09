using System.Collections.ObjectModel;

namespace Biolog.Features.EvidenceComparison.Model;

/// <summary>
/// One field compared across whichever sources supplied a value for it (BIO-DOC-008-AC01):
/// source-by-source values plus the derived <see cref="Status"/>. Computed only by
/// <see cref="ComparisonEngine.Compare"/>, never caller-constructed - <see cref="Status"/> is a
/// trust decision downstream code relies on.
///
/// <para>
/// <b>Locked down against post-hoc tampering, including the dictionary itself (the BIO-DOC-007
/// QA-02/QA-03 lesson, applied proactively here rather than waiting for a reviewer to find it):</b>
/// <see cref="ValuesBySource"/> is wrapped in <see cref="ReadOnlyDictionary{TKey,TValue}"/> over a
/// freshly-copied <see cref="Dictionary{TKey,TValue}"/> - a genuinely distinct type from
/// <see cref="Dictionary{TKey,TValue}"/> that cannot be downcast back to one, not merely the
/// caller's own dictionary viewed through <see cref="IReadOnlyDictionary{TKey,TValue}"/> (which
/// would still be a real, mutable <see cref="Dictionary{TKey,TValue}"/> underneath, exactly the
/// gap BIO-DOC-007-QA-03 found).
/// </para>
/// </summary>
public sealed record FieldComparison
{
    public string FieldName { get; internal init; } = null!;
    public IReadOnlyDictionary<EvidenceSourceKind, SourceFieldValue> ValuesBySource { get; internal init; } =
        new ReadOnlyDictionary<EvidenceSourceKind, SourceFieldValue>(new Dictionary<EvidenceSourceKind, SourceFieldValue>());
    public FieldMatchStatus Status { get; internal init; }

    internal FieldComparison(string fieldName, IReadOnlyDictionary<EvidenceSourceKind, SourceFieldValue> valuesBySource, FieldMatchStatus status)
    {
        FieldName = fieldName;
        ValuesBySource = new ReadOnlyDictionary<EvidenceSourceKind, SourceFieldValue>(
            new Dictionary<EvidenceSourceKind, SourceFieldValue>(valuesBySource));
        Status = status;
    }
}

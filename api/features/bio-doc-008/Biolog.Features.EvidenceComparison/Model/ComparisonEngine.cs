namespace Biolog.Features.EvidenceComparison.Model;

/// <summary>
/// Compares one field's values across whichever sources supplied one (BIO-DOC-008-AC01). The
/// equality policy is deliberately simple and stated explicitly, not left implicit: two sources
/// match when their <see cref="SourceFieldValue.NormalizedValue"/> strings are exactly equal
/// (ordinal, case-sensitive) - normalization is each source's own job (<c>MrzFieldRead</c>'s
/// engine-computed normalized value, etc.), so this engine trusts normalized values are already
/// in a canonical comparable form rather than re-normalizing them itself.
/// </summary>
public static class ComparisonEngine
{
    public static FieldComparison Compare(string fieldName, IReadOnlyDictionary<EvidenceSourceKind, SourceFieldValue> valuesBySource)
    {
        if (valuesBySource.Count < 2)
        {
            // Fewer than two sources means there is nothing to agree or disagree - never
            // reported as a trivial "match" just because nothing contradicts it (AC01).
            return new FieldComparison(fieldName, valuesBySource, FieldMatchStatus.Unknown);
        }

        var distinctValues = valuesBySource.Values
            .Select(v => v.NormalizedValue)
            .Distinct(StringComparer.Ordinal)
            .Count();

        var status = distinctValues == 1 ? FieldMatchStatus.Match : FieldMatchStatus.Mismatch;
        return new FieldComparison(fieldName, valuesBySource, status);
    }
}

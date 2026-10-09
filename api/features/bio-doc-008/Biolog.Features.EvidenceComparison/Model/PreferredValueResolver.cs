namespace Biolog.Features.EvidenceComparison.Model;

/// <summary>
/// The ONLY way to obtain a single "preferred" value from a <see cref="FieldComparison"/>
/// (BIO-DOC-008-AC04). Requires an explicit <see cref="SourcePrecedencePolicy"/> - there is no
/// overload, default parameter, or other path anywhere in this engine that resolves a preferred
/// value without one, so a source can never be silently preferred.
/// </summary>
public static class PreferredValueResolver
{
    /// <summary>Returns the value from the highest-precedence source in
    /// <paramref name="policy"/> that actually contributed a value to
    /// <paramref name="comparison"/>, or <c>null</c> if none of the sources named in the policy
    /// supplied one - never a guess at a source the policy never named.</summary>
    public static SourceFieldValue? ResolvePreferredValue(FieldComparison comparison, SourcePrecedencePolicy policy)
    {
        foreach (var source in policy.OrderedPreference)
        {
            if (comparison.ValuesBySource.TryGetValue(source, out var value))
            {
                return value;
            }
        }

        return null;
    }
}

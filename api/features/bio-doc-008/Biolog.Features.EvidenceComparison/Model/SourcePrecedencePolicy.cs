namespace Biolog.Features.EvidenceComparison.Model;

/// <summary>
/// An explicit, caller-declared order of preference among sources (BIO-DOC-008-AC04: "no source is
/// silently preferred without documented policy"). This is the ONLY input
/// <see cref="PreferredValueResolver.ResolvePreferredValue"/> accepts to pick a single value from
/// a <see cref="FieldComparison"/> - there is no default, implicit, or hard-coded precedence
/// anywhere in this engine. A caller that wants "MRZ over barcode over visual" must construct this
/// policy explicitly and pass it in, which is itself the "documented policy" AC04 asks for: the
/// precedence lives in the caller's own code/configuration, never silently inside this engine.
/// </summary>
public sealed record SourcePrecedencePolicy
{
    public IReadOnlyList<EvidenceSourceKind> OrderedPreference { get; }

    private SourcePrecedencePolicy(IReadOnlyList<EvidenceSourceKind> orderedPreference) =>
        OrderedPreference = orderedPreference;

    public static SourcePrecedencePolicy Create(IReadOnlyList<EvidenceSourceKind> orderedPreference)
    {
        if (orderedPreference.Count == 0)
        {
            throw new ArgumentException("a precedence policy must name at least one source", nameof(orderedPreference));
        }

        if (orderedPreference.Distinct().Count() != orderedPreference.Count)
        {
            throw new ArgumentException("a precedence policy must not repeat a source", nameof(orderedPreference));
        }

        // A fresh copy wrapped in AsReadOnly() (a ReadOnlyCollection<T>, not downcastable back to
        // List<T>) - the same BIO-DOC-007-QA-02/QA-03 lesson applied proactively here, not just to
        // this engine's own computed results but to caller-supplied policy input too.
        return new SourcePrecedencePolicy(new List<EvidenceSourceKind>(orderedPreference).AsReadOnly());
    }
}

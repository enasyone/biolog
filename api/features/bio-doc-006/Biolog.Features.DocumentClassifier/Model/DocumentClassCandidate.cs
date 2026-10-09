namespace Biolog.Features.DocumentClassifier.Model;

/// <summary>
/// One ranked classification candidate (BIO-DOC-006-AC01: "ranked candidates with confidence").
/// <see cref="Confidence"/> is the trust surface a caller ranks and gates on (AC02/AC05), so - the
/// same reasoning as every prior BIO-DOC-00x engine result this epic - every accessor is
/// <c>internal init</c> with an explicit <c>internal</c> constructor: a public <c>init</c> would
/// let a caller launder a low-confidence candidate into a high-confidence one via <c>with</c>
/// without <see cref="Classifier"/>'s own scoring ever running.
/// </summary>
public sealed record DocumentClassCandidate
{
    public string Country { get; internal init; } = null!;
    public string DocumentClass { get; internal init; } = null!;
    public string Series { get; internal init; } = null!;
    public double Confidence { get; internal init; }
    public IReadOnlyList<string> MatchedSignals { get; internal init; } = [];
    public DocumentIntelligenceRegistryEntry RegistryEntry { get; internal init; } = null!;

    internal DocumentClassCandidate(
        string country,
        string documentClass,
        string series,
        double confidence,
        IReadOnlyList<string> matchedSignals,
        DocumentIntelligenceRegistryEntry registryEntry)
    {
        Country = country;
        DocumentClass = documentClass;
        Series = series;
        Confidence = confidence;
        MatchedSignals = matchedSignals;
        RegistryEntry = registryEntry;
    }
}

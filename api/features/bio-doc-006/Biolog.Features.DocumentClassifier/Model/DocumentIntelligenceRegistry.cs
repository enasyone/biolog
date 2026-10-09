namespace Biolog.Features.DocumentClassifier.Model;

/// <summary>Thrown when <see cref="DocumentIntelligenceRegistry.Register"/> is given an entry
/// whose <see cref="DocumentIntelligenceRegistryEntry.VersionNumber"/> does not move the catalog
/// forward for that (Country, DocumentClass, Series, Side) key.</summary>
public sealed class StaleRegistryVersionException(string message) : InvalidOperationException(message);

/// <summary>
/// The Document Intelligence Registry (BIO-DOC-006-AC03): the versioned catalog mapping a
/// classified document identity to the template used to read it. Data-driven, not a hard-coded
/// per-document switch - the same "configured registry" shape BIO-DOC-002's
/// <c>MrzFormatRegistry</c>, BIO-DOC-003's <c>BarcodeDecoderRegistry</c> and BIO-DOC-004's
/// <c>OcrTemplateRegistry</c> already established. Ships with no pre-registered entries; a caller
/// registers the document identities relevant to the documents it actually processes.
///
/// <para>
/// <b>"Versioned" here is deliberately a simpler discipline than ADR-0001 Decision 3's
/// optimistic-concurrency compare-and-set</b> (the pattern <c>DocumentSession.Version</c> and
/// BIO-DOC-005's <c>ParserRecord.ConcurrencyVersion</c> use): this registry is a platform-wide
/// reference catalog populated by configuration, not a mutable per-request aggregate multiple
/// tenants write to concurrently, so there is no concurrent-writer race for a CAS to guard
/// against. What AC03's "versioned" wording still buys, and what this registry enforces, is a
/// monotonic-version guarantee: <see cref="Register"/> rejects an entry that does not strictly
/// increase <see cref="DocumentIntelligenceRegistryEntry.VersionNumber"/> for its own
/// (Country, DocumentClass, Series, Side) key, so a catalog update can never silently regress a
/// document identity to an older template mapping.
/// </para>
/// </summary>
public sealed class DocumentIntelligenceRegistry
{
    private readonly Dictionary<(string Country, string DocumentClass, string Series, DocumentSide Side), DocumentIntelligenceRegistryEntry> _byIdentity = [];
    private readonly Dictionary<string, DocumentIntelligenceRegistryEntry> _byTemplateId = [];

    public void Register(DocumentIntelligenceRegistryEntry entry)
    {
        var key = (entry.Country, entry.DocumentClass, entry.Series, entry.Side);
        if (_byIdentity.TryGetValue(key, out var existing))
        {
            if (entry.VersionNumber <= existing.VersionNumber)
            {
                throw new StaleRegistryVersionException(
                    $"entry '{entry.EntryId}' version {entry.VersionNumber} does not move {key} forward from version {existing.VersionNumber}");
            }

            // The superseded version's template must stop resolving via GetByTemplateId, or a
            // classification signal keyed by the retired template could still select the stale
            // identity mapping (BIO-DOC-006-QA-01, Chioma's acceptance review) - the reverse
            // index has to be updated atomically with the identity entry, not left to accumulate
            // every template a key has ever pointed at.
            if (existing.TemplateId != entry.TemplateId
                && _byTemplateId.TryGetValue(existing.TemplateId, out var indexed)
                && ReferenceEquals(indexed, existing))
            {
                _byTemplateId.Remove(existing.TemplateId);
            }
        }

        // A TemplateId can identify at most one CURRENT document identity at a time - two
        // distinct identities silently sharing one template made Match and GetByTemplateId
        // disagree about which identity was current (BIO-DOC-006-QA-02, Chioma's second
        // acceptance review): both stayed in _byIdentity, but classification (which resolves
        // only through GetByTemplateId) always followed the LAST registration, regardless of
        // which identity a caller thought was still current. If this TemplateId already belongs
        // to a different identity, that identity's currency is explicitly and atomically
        // transferred away here - evicted from _byIdentity in the same call that installs the
        // new owner, never left to silently coexist as a second "current" entry nothing can
        // actually reach via classification.
        if (_byTemplateId.TryGetValue(entry.TemplateId, out var templateOwner))
        {
            var ownerKey = (templateOwner.Country, templateOwner.DocumentClass, templateOwner.Series, templateOwner.Side);
            if (ownerKey != key
                && _byIdentity.TryGetValue(ownerKey, out var ownerCurrent)
                && ReferenceEquals(ownerCurrent, templateOwner))
            {
                _byIdentity.Remove(ownerKey);
            }
        }

        _byIdentity[key] = entry;
        _byTemplateId[entry.TemplateId] = entry;
    }

    /// <summary>Resolves the current (highest-version) entry for an exact classified identity, or
    /// <c>null</c> if nothing is registered for it - never a best-effort guess at a near match.</summary>
    public DocumentIntelligenceRegistryEntry? Match(string country, string documentClass, string series, DocumentSide side) =>
        _byIdentity.GetValueOrDefault((country, documentClass, series, side));

    /// <summary>Resolves the entry that declares a given OCR template as its own - the reverse
    /// lookup <see cref="Classifier"/> uses to turn "this template matched" evidence back
    /// into a full classified identity.</summary>
    public DocumentIntelligenceRegistryEntry? GetByTemplateId(string templateId) =>
        _byTemplateId.GetValueOrDefault(templateId);

    public IReadOnlyList<DocumentIntelligenceRegistryEntry> All => _byIdentity.Values.ToList();
}

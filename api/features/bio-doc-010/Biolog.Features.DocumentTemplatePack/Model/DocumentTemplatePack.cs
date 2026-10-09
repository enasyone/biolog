using System.Globalization;
using System.Text.RegularExpressions;
using Biolog.Features.DocumentClassifier.Model;
using Biolog.Features.DocumentTemplatePack.Audit;

namespace Biolog.Features.DocumentTemplatePack.Model;

/// <summary>
/// A governed, versioned release of <see cref="DocumentTemplatePackEntry"/>s (AC01 through AC03) -
/// "the Nigeria Document Intelligence Pack v1" is one instance of this type. Starts
/// <see cref="DocumentTemplatePackStatus.Draft"/> so entries can be assembled; <see cref="Publish"/>
/// is the one-way transition to <see cref="DocumentTemplatePackStatus.Published"/> (AC02) that also
/// registers every entry's sides into a caller-supplied <see cref="DocumentIntelligenceRegistry"/>
/// (BIO-DOC-006) - publishing this pack IS what makes its governed identities resolvable to a
/// classifier.
///
/// <para>
/// <b>"Immutable once published" (AC02) is enforced two ways:</b> <see cref="AddEntry"/> and a
/// second <see cref="Publish"/> call both throw once <see cref="Status"/> is
/// <see cref="DocumentTemplatePackStatus.Published"/>, and there is no <c>RemoveEntry</c> or
/// <c>ReplaceEntry</c> method at any lifecycle stage - an entry is added once, never edited in
/// place (ADR-0001 S3.3). Correcting a mistake means constructing a new
/// <see cref="Model.DocumentTemplatePack"/> with a new <see cref="PackVersion"/> that supersedes
/// this one; the old, published pack instance stays exactly as it was, readable by whatever store
/// holds it (the same "the original stays readable" discipline as
/// <see cref="DocumentIntelligenceRegistry"/>'s own monotonic-version rule).
/// </para>
/// </summary>
public sealed partial class DocumentTemplatePack
{
    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d+)?(Z|[+-]\d{2}:\d{2})$")]
    private static partial Regex Rfc3339Pattern();

    private readonly Dictionary<string, DocumentTemplatePackEntry> _entriesById = [];

    public string PackId { get; }
    public string PackVersion { get; }
    public string CountryScope { get; }
    public DocumentTemplatePackStatus Status { get; private set; } = DocumentTemplatePackStatus.Draft;
    public string? PublishedBy { get; private set; }
    public string? PublishedAt { get; private set; }

    public IReadOnlyList<DocumentTemplatePackEntry> Entries => [.. _entriesById.Values];

    public DocumentTemplatePack(string packId, string packVersion, string countryScope)
    {
        if (string.IsNullOrWhiteSpace(packId))
        {
            throw new ArgumentException("packId must not be blank", nameof(packId));
        }

        if (string.IsNullOrWhiteSpace(packVersion))
        {
            throw new ArgumentException("packVersion must not be blank", nameof(packVersion));
        }

        if (string.IsNullOrWhiteSpace(countryScope))
        {
            throw new ArgumentException("countryScope must not be blank", nameof(countryScope));
        }

        PackId = packId;
        PackVersion = packVersion;
        CountryScope = countryScope;
    }

    public void AddEntry(DocumentTemplatePackEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (Status == DocumentTemplatePackStatus.Published)
        {
            throw new DocumentTemplatePackAlreadyPublishedException(
                $"pack '{PackId}' version '{PackVersion}' is already published - entries cannot be added");
        }

        if (_entriesById.ContainsKey(entry.EntryId))
        {
            throw new DuplicatePackEntryException(
                $"pack '{PackId}' already has an entry with id '{entry.EntryId}' - construct a new entry/pack version instead of replacing one in place");
        }

        _entriesById[entry.EntryId] = entry;
    }

    public DocumentTemplatePackEntry? FindEntry(string entryId) => _entriesById.GetValueOrDefault(entryId);

    /// <summary>Resolves the entry that declares a given BIO-DOC-006 template id as one of its own
    /// sides - the reverse lookup <see cref="Store.IDocumentTemplatePackStore.FindByTemplateId"/>
    /// uses.</summary>
    public DocumentTemplatePackEntry? FindEntryByTemplateId(string templateId) =>
        _entriesById.Values.FirstOrDefault(e => e.Sides.Any(s => s.TemplateId == templateId));

    /// <summary>
    /// AC02's one-way transition. Registers every entry's sides into <paramref name="registry"/>
    /// (BIO-DOC-006), writes an audit event, then flips <see cref="Status"/> - the same
    /// audit-before-commit ordering every hard-audited BIOLOG feature in this epic uses, so a
    /// pack is never left claiming to be published with no durable record of it.
    /// </summary>
    public void Publish(
        string publishedBy,
        string publishedAt,
        string correlationId,
        DocumentIntelligenceRegistry registry,
        IDocumentTemplatePackAuditSink auditSink)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(auditSink);

        if (string.IsNullOrWhiteSpace(publishedBy))
        {
            throw new ArgumentException("publishedBy must not be blank", nameof(publishedBy));
        }

        if (!Rfc3339Pattern().IsMatch(publishedAt)
            || !DateTimeOffset.TryParse(publishedAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            throw new ArgumentException(
                $"publishedAt must be a full RFC 3339 date-time with an explicit 'Z' or numeric offset, got '{publishedAt}'",
                nameof(publishedAt));
        }

        if (string.IsNullOrWhiteSpace(correlationId))
        {
            throw new ArgumentException("correlationId must not be blank", nameof(correlationId));
        }

        if (Status == DocumentTemplatePackStatus.Published)
        {
            throw new DocumentTemplatePackAlreadyPublishedException(
                $"pack '{PackId}' version '{PackVersion}' is already published");
        }

        if (_entriesById.Count == 0)
        {
            throw new EmptyPackCannotBePublishedException(
                $"pack '{PackId}' version '{PackVersion}' has no entries - a governed pack must govern at least one document identity");
        }

        foreach (var entry in _entriesById.Values)
        {
            foreach (var side in entry.Sides)
            {
                registry.Register(new DocumentIntelligenceRegistryEntry(
                    EntryId: $"{entry.EntryId}:{side.Side}",
                    Country: entry.Country,
                    DocumentClass: entry.DocumentClass,
                    Series: entry.Series,
                    Side: side.Side,
                    VersionNumber: entry.VersionNumber,
                    TemplateId: side.TemplateId,
                    ExpectedSymbologies: []));
            }
        }

        var actor = new AuditActor(ActorType.Unknown, publishedBy);
        var auditEvent = DocumentTemplatePackAuditEventBuilder.BuildPublished(actor, PackId, PackVersion, _entriesById.Count, correlationId);
        WriteAuditEventOrThrow(auditSink, auditEvent);

        Status = DocumentTemplatePackStatus.Published;
        PublishedBy = publishedBy;
        PublishedAt = publishedAt;
    }

    /// <summary>The same ambiguous-write resolution every hard-audited BIOLOG feature's own
    /// <c>AuditAsync</c> helper uses.</summary>
    private static void WriteAuditEventOrThrow(IDocumentTemplatePackAuditSink auditSink, DocumentTemplatePackAuditEvent auditEvent)
    {
        try
        {
            auditSink.WriteAsync(auditEvent).GetAwaiter().GetResult();
            return;
        }
        catch (Exception)
        {
            // Ambiguous - fall through to verify rather than assume failure.
        }

        bool exists;
        try
        {
            exists = auditSink.ExistsAsync(auditEvent.EventId).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            throw new DocumentTemplatePackAuditUnavailableException("audit sink unavailable: could not even verify a prior write", ex);
        }

        if (!exists)
        {
            throw new DocumentTemplatePackAuditUnavailableException("audit sink unavailable: write did not durably persist");
        }
    }
}

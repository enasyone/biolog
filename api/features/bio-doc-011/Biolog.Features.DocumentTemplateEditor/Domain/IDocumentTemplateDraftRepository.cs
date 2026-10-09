using System.Collections.Concurrent;

namespace Biolog.Features.DocumentTemplateEditor.Domain;

/// <summary>
/// In-memory placeholder (not the real migration-backed store the master specification
/// targets), the same seam every sibling story's own repository already establishes. Keyed by
/// <c>(TenantId, TemplateId, Version)</c> - <see cref="DocumentTemplateDraft.Version"/> never
/// changes once a row is created. Mirrors BIO-CRD-008's own <c>ICardTemplateRepository</c>.
///
/// <b>Round-2 change (Zainab, ZB-DOC011-02, High):</b> round 1's own repository-level
/// compare-and-set (<c>TryUpdate(expectedRevision)</c>) let two ordering bugs through - it is
/// replaced by <see cref="DocumentTemplateDraftService"/> serializing every mutation to a lineage
/// through <see cref="DocumentTemplateDraftLockRegistry"/> instead. Under that lock there is only
/// ever one writer for a given (tenant, templateId), so this repository no longer needs to detect
/// or reject a racing writer itself - <see cref="Update"/> is a plain, unconditional replace. The
/// service still checks the caller's own asserted revision against the current row (while holding
/// the lock) to surface staleness to a caller whose own view was out of date - that is a
/// client-communication concern, not a concurrency-safety one anymore.
/// </summary>
public interface IDocumentTemplateDraftRepository
{
    /// <summary>Null if this tenant/lineage/version does not exist.</summary>
    DocumentTemplateDraft? GetVersion(string tenantId, string templateId, int version);

    /// <summary>AC04/AC05: null if this lineage has no currently-published version for this tenant. Never returns a Draft/Test/Approval row.</summary>
    DocumentTemplateDraft? GetPublished(string tenantId, string templateId);

    /// <summary>Empty if this lineage does not exist for this tenant. Ordered by version ascending.</summary>
    IReadOnlyList<DocumentTemplateDraft> ListVersions(string tenantId, string templateId);

    /// <summary>Inserts a brand-new row (a first draft, or a new version). Throws if this exact (tenant, templateId, version) already exists.</summary>
    DocumentTemplateDraft Add(DocumentTemplateDraft draft);

    /// <summary>Unconditional replace of an existing row. Callers must hold <see cref="DocumentTemplateDraftLockRegistry"/>'s own lock for this (tenant, templateId) - see this interface's own header.</summary>
    void Update(DocumentTemplateDraft updated);
}

public sealed class InMemoryDocumentTemplateDraftRepository : IDocumentTemplateDraftRepository
{
    private readonly ConcurrentDictionary<string, DocumentTemplateDraft> _byKey = new(StringComparer.Ordinal);

    public DocumentTemplateDraft? GetVersion(string tenantId, string templateId, int version) =>
        _byKey.TryGetValue(Key(tenantId, templateId, version), out var draft) ? draft : null;

    public DocumentTemplateDraft? GetPublished(string tenantId, string templateId) =>
        _byKey.Values.SingleOrDefault(d => d.TenantId == tenantId && d.TemplateId == templateId && d.Status == DocumentTemplateDraftStatus.Published);

    public IReadOnlyList<DocumentTemplateDraft> ListVersions(string tenantId, string templateId) =>
        [.. _byKey.Values.Where(d => d.TenantId == tenantId && d.TemplateId == templateId).OrderBy(d => d.Version)];

    public DocumentTemplateDraft Add(DocumentTemplateDraft draft)
    {
        if (!_byKey.TryAdd(Key(draft.TenantId, draft.TemplateId, draft.Version), draft))
        {
            throw new InvalidOperationException($"a document template draft row already exists for {draft.TenantId}/{draft.TemplateId}/v{draft.Version}");
        }

        return draft;
    }

    public void Update(DocumentTemplateDraft updated)
    {
        var key = Key(updated.TenantId, updated.TemplateId, updated.Version);
        _byKey[key] = updated;
    }

    private static string Key(string tenantId, string templateId, int version) => $"{tenantId}\u0000{templateId}\u0000{version}";
}

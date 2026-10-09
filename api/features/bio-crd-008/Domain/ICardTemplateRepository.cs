using System.Collections.Concurrent;

namespace Biolog.Features.CardTemplates.Domain;

/// <summary>Tenant-scoped summary of one template lineage, for a picker UI - never guesses at a published version, only ever reports one that genuinely exists.</summary>
public sealed record CardTemplateLineageSummary
{
    public required string TemplateId { get; init; }
    public required string Name { get; init; }
    public required int LatestVersion { get; init; }
    public required CardTemplateStatus LatestStatus { get; init; }
    public int? PublishedVersion { get; init; }
}

/// <summary>
/// In-memory placeholder (not the real migration-backed store the master specification
/// targets), the same seam every sibling story's own repository already establishes. Keyed by
/// <c>(TenantId, TemplateId, Version)</c> - <see cref="Version"/> never changes once a row is
/// created (AC04's own "exact published version" identity), so no CAS is needed on that part of
/// the key, only on the row's own <see cref="CardTemplate.Revision"/> for in-place edits.
/// </summary>
public interface ICardTemplateRepository
{
    /// <summary>Null if this tenant/lineage/version does not exist.</summary>
    CardTemplate? GetVersion(string tenantId, string templateId, int version);

    /// <summary>Null if this lineage has no currently-published version for this tenant.</summary>
    CardTemplate? GetPublished(string tenantId, string templateId);

    /// <summary>Empty if this lineage does not exist for this tenant. Ordered by version ascending.</summary>
    IReadOnlyList<CardTemplate> ListVersions(string tenantId, string templateId);

    /// <summary>Every lineage for this tenant, one summary row each, ordered by name.</summary>
    IReadOnlyList<CardTemplateLineageSummary> ListLineages(string tenantId);

    /// <summary>Inserts a brand-new row (a first draft, or a new version). Throws if this exact (tenant, templateId, version) already exists.</summary>
    CardTemplate Add(CardTemplate template);

    /// <summary>Compare-and-set update of an existing row by its own <see cref="CardTemplate.Revision"/> - <paramref name="expectedRevision"/> is the revision BEFORE <paramref name="updated"/>'s own change. Returns false on a stale/mismatched revision.</summary>
    bool TryUpdate(CardTemplate updated, int expectedRevision);
}

public sealed class InMemoryCardTemplateRepository : ICardTemplateRepository
{
    private readonly ConcurrentDictionary<string, CardTemplate> _byKey = new(StringComparer.Ordinal);

    public CardTemplate? GetVersion(string tenantId, string templateId, int version) =>
        _byKey.TryGetValue(Key(tenantId, templateId, version), out var template) ? template : null;

    public CardTemplate? GetPublished(string tenantId, string templateId) =>
        _byKey.Values.SingleOrDefault(t => t.TenantId == tenantId && t.TemplateId == templateId && t.Status == CardTemplateStatus.Published);

    public IReadOnlyList<CardTemplate> ListVersions(string tenantId, string templateId) =>
        [.. _byKey.Values.Where(t => t.TenantId == tenantId && t.TemplateId == templateId).OrderBy(t => t.Version)];

    public IReadOnlyList<CardTemplateLineageSummary> ListLineages(string tenantId)
    {
        return [.. _byKey.Values
            .Where(t => t.TenantId == tenantId)
            .GroupBy(t => t.TemplateId)
            .Select(group =>
            {
                var latest = group.OrderByDescending(t => t.Version).First();
                var published = group.FirstOrDefault(t => t.Status == CardTemplateStatus.Published);
                return new CardTemplateLineageSummary
                {
                    TemplateId = latest.TemplateId,
                    Name = latest.Name,
                    LatestVersion = latest.Version,
                    LatestStatus = latest.Status,
                    PublishedVersion = published?.Version,
                };
            })
            .OrderBy(s => s.Name, StringComparer.Ordinal)];
    }

    public CardTemplate Add(CardTemplate template)
    {
        if (!_byKey.TryAdd(Key(template.TenantId, template.TemplateId, template.Version), template))
        {
            throw new InvalidOperationException($"a card template row already exists for {template.TenantId}/{template.TemplateId}/v{template.Version}");
        }

        return template;
    }

    public bool TryUpdate(CardTemplate updated, int expectedRevision)
    {
        var key = Key(updated.TenantId, updated.TemplateId, updated.Version);
        if (!_byKey.TryGetValue(key, out var current) || current.Revision != expectedRevision)
        {
            return false;
        }

        return _byKey.TryUpdate(key, updated, current);
    }

    private static string Key(string tenantId, string templateId, int version) => $"{tenantId}\u0000{templateId}\u0000{version}";
}

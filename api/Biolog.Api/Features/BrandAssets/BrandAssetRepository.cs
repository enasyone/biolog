using System.Collections.Concurrent;

namespace Biolog.Api.Features.BrandAssets;

public sealed record BrandAssetListFilter(
    BrandAssetType? AssetType, BrandAssetStatus? Status, string? OrganisationId, string? CampusId);

public sealed record BrandAssetPage(IReadOnlyList<BrandAsset> Items, string? NextCursor);

/// <summary>
/// Tenant-scoped brand asset storage (BIO-CRD-001-AC02, AC05). Every read/write takes an
/// explicit <c>tenantId</c> and only ever touches that tenant's rows - there is no
/// query that can return, update or delete another tenant's asset, structurally, not by
/// a caller remembering to add a WHERE clause.
///
/// <b>This is an in-memory placeholder, not the tenant/control-plane MySQL store the master
/// specification targets.</b> No prior story has wired real database access into
/// <c>api/Biolog.Api</c> yet, and ADR-0005 requires every schema migration to go through a
/// single Kemi-held <c>migration-core</c> lock and sequence assignment - a genuinely
/// cross-cutting process this story's own conflict group (<c>brand-assets</c>) has no
/// standing to unilaterally start. This repository is scoped so that a real, migration-backed
/// implementation is a drop-in replacement behind <see cref="IBrandAssetRepository"/>:
/// nothing above this interface (validation, DPI/SVG/malware checks, the HTTP layer)
/// depends on it being in-memory.
/// </summary>
public interface IBrandAssetRepository
{
    void Add(BrandAsset asset);

    /// <summary>Null if the asset does not exist OR belongs to a different tenant (BIO-CRD-001-AC05) - the two are indistinguishable to a caller, deliberately, matching ADR-0002's 404-not-403 anti-enumeration posture.</summary>
    BrandAsset? GetById(string tenantId, string id);

    /// <summary>The highest version currently stored for this lineage, scoped to the tenant. Null if the lineage does not exist for this tenant.</summary>
    BrandAsset? GetLatestVersion(string tenantId, string lineageId);

    BrandAssetPage List(string tenantId, BrandAssetListFilter filter, int limit, string? cursor);

    /// <summary>Returns the updated record, or null if it does not exist for this tenant.</summary>
    BrandAsset? UpdateStatus(string tenantId, string id, BrandAssetStatus newStatus);
}

public sealed class InMemoryBrandAssetRepository : IBrandAssetRepository
{
    private readonly ConcurrentDictionary<string, BrandAsset> _byId = new(StringComparer.Ordinal);

    public void Add(BrandAsset asset)
    {
        if (!_byId.TryAdd(asset.Id, asset))
        {
            throw new InvalidOperationException($"duplicate brand asset id: {asset.Id}");
        }
    }

    public BrandAsset? GetById(string tenantId, string id)
    {
        return _byId.TryGetValue(id, out var asset) && asset.TenantId == tenantId ? asset : null;
    }

    public BrandAsset? GetLatestVersion(string tenantId, string lineageId)
    {
        return _byId.Values
            .Where(a => a.TenantId == tenantId && a.LineageId == lineageId)
            .OrderByDescending(a => a.Version)
            .FirstOrDefault();
    }

    public BrandAssetPage List(string tenantId, BrandAssetListFilter filter, int limit, string? cursor)
    {
        var ordered = _byId.Values
            .Where(a => a.TenantId == tenantId)
            .Where(a => filter.AssetType is null || a.AssetType == filter.AssetType)
            .Where(a => filter.Status is null || a.Status == filter.Status)
            .Where(a => filter.OrganisationId is null || a.OrganisationId == filter.OrganisationId)
            .Where(a => filter.CampusId is null || a.CampusId == filter.CampusId)
            .OrderByDescending(a => a.CreatedAtUtc)
            .ThenBy(a => a.Id, StringComparer.Ordinal)
            .ToList();

        var startIndex = 0;
        if (cursor is not null)
        {
            var afterId = BrandAssetCursor.Decode(cursor);
            var position = ordered.FindIndex(a => a.Id == afterId);
            startIndex = position >= 0 ? position + 1 : ordered.Count;
        }

        var page = ordered.Skip(startIndex).Take(limit).ToList();
        var hasMore = startIndex + page.Count < ordered.Count;
        var nextCursor = hasMore && page.Count > 0 ? BrandAssetCursor.Encode(page[^1].Id) : null;
        return new BrandAssetPage(page, nextCursor);
    }

    public BrandAsset? UpdateStatus(string tenantId, string id, BrandAssetStatus newStatus)
    {
        while (true)
        {
            if (!_byId.TryGetValue(id, out var current) || current.TenantId != tenantId)
            {
                return null;
            }
            var updated = current with { Status = newStatus };
            if (_byId.TryUpdate(id, updated, current))
            {
                return updated;
            }
            // Someone else updated concurrently; retry against the fresh value (optimistic loop).
        }
    }
}

/// <summary>Opaque cursor (ADR-0004 §5.1): callers echo it back verbatim and never construct/parse it themselves.</summary>
public static class BrandAssetCursor
{
    public static string Encode(string lastId) => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(lastId));

    public static string Decode(string cursor)
    {
        try
        {
            return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
        }
        catch (FormatException)
        {
            throw new ValidationFailedException([new ValidationErrorItem("cursor", "invalid_format", "cursor is not a valid opaque token")]);
        }
    }
}

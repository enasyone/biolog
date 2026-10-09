using System.Collections.Concurrent;

namespace Biolog.Features.CardObjects.Domain;

/// <summary>
/// Tenant-scoped signature-asset storage (BIO-CRD-004-AC04). Follows BIO-CRD-001's own
/// <c>InMemoryBrandAssetRepository</c> precedent within this same epic - a lean, tenant-scoped,
/// in-memory, optimistic-versioning store - rather than BIO-PHO-001's heavier audit-atomicity/
/// lock-registry apparatus, since no AC here asks for an audit trail and no cross-story identity
/// existence check is declared as a dependency.
///
/// <b>This is an in-memory placeholder, not the tenant/control-plane MySQL store the master
/// specification targets</b> (ADR-0005: real schema migrations go through a single Kemi-held
/// lock this story's own conflict group has no standing to start). Scoped so a real,
/// migration-backed implementation is a drop-in replacement behind this interface.
/// </summary>
public interface ISignatureAssetRepository
{
    /// <summary>Null if this lineage does not exist for this tenant.</summary>
    SignatureAsset? GetLatestVersion(string tenantId, string lineageId);

    /// <summary>Null if this exact version does not exist for this tenant/lineage.</summary>
    SignatureAsset? GetVersion(string tenantId, string lineageId, int version);

    /// <summary>Empty if this lineage does not exist for this tenant. Ordered by version ascending.</summary>
    IReadOnlyList<SignatureAsset> ListVersions(string tenantId, string lineageId);

    /// <summary>Appends a new, higher-numbered version for this tenant/lineage - never mutates an existing row.</summary>
    SignatureAsset AppendVersion(string tenantId, string lineageId, byte[] content, string createdAt);
}

public sealed class InMemorySignatureAssetRepository : ISignatureAssetRepository
{
    private readonly ConcurrentDictionary<string, SignatureAsset> _byKey = new(StringComparer.Ordinal);

    public SignatureAsset? GetLatestVersion(string tenantId, string lineageId)
    {
        return _byKey.Values
            .Where(a => a.TenantId == tenantId && a.LineageId == lineageId)
            .OrderByDescending(a => a.Version)
            .FirstOrDefault();
    }

    public SignatureAsset? GetVersion(string tenantId, string lineageId, int version)
    {
        return _byKey.TryGetValue(Key(tenantId, lineageId, version), out var asset) ? asset : null;
    }

    public IReadOnlyList<SignatureAsset> ListVersions(string tenantId, string lineageId)
    {
        return _byKey.Values
            .Where(a => a.TenantId == tenantId && a.LineageId == lineageId)
            .OrderBy(a => a.Version)
            .ToList();
    }

    public SignatureAsset AppendVersion(string tenantId, string lineageId, byte[] content, string createdAt)
    {
        while (true)
        {
            var latest = GetLatestVersion(tenantId, lineageId);
            var nextVersion = (latest?.Version ?? 0) + 1;
            var asset = new SignatureAsset(tenantId, lineageId, nextVersion, content, createdAt);

            if (_byKey.TryAdd(Key(tenantId, lineageId, nextVersion), asset))
            {
                return asset;
            }

            // Someone else appended the same next version concurrently; retry against a fresh read.
        }
    }

    private static string Key(string tenantId, string lineageId, int version) => $"{tenantId}\u0000{lineageId}\u0000{version}";
}

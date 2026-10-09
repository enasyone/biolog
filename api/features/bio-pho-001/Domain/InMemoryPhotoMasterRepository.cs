using System.Collections.Concurrent;

namespace Biolog.Features.Photography.Domain;

/// <summary>
/// In-memory placeholder for <see cref="IPhotoMasterRepository"/>, pending real MySQL-backed
/// persistence (ADR-0005: every schema migration goes through Kemi's <c>migration-core</c>
/// lock, a cross-cutting process this story's own <c>photo-core</c> conflict group has no
/// standing to start unilaterally). A real, migration-backed implementation is a drop-in
/// replacement behind this interface - nothing above it depends on being in-memory. Same
/// precedent as <c>InMemoryBrandAssetRepository</c> (BIO-CRD-001) and every sibling
/// identity-wave story's own in-memory repository.
/// </summary>
internal sealed class InMemoryPhotoMasterRepository : IPhotoMasterRepository
{
    // Keyed by (TenantId, PersonId) -> version -> master, so a foreign-tenant lookup can never
    // even reach another tenant's rows.
    private readonly ConcurrentDictionary<(string TenantId, string PersonId), ConcurrentDictionary<int, PhotoMaster>> _byPerson = new();

    public Task<PhotoMaster> AppendAsync(PhotoMaster master, CancellationToken cancellationToken = default)
    {
        var versions = _byPerson.GetOrAdd((master.TenantId, master.PersonId), static _ => new ConcurrentDictionary<int, PhotoMaster>());
        if (!versions.TryAdd(master.Version, master))
        {
            throw PhotoError.MasterVersionAlreadyExists(master.PersonId, master.Version);
        }

        return Task.FromResult(master);
    }

    public Task<PhotoMaster?> GetLatestAsync(string tenantId, string personId, CancellationToken cancellationToken = default)
    {
        if (!_byPerson.TryGetValue((tenantId, personId), out var versions) || versions.IsEmpty)
        {
            return Task.FromResult<PhotoMaster?>(null);
        }

        return Task.FromResult<PhotoMaster?>(versions.Values.OrderByDescending(m => m.Version).First());
    }

    public Task<PhotoMaster?> GetVersionAsync(string tenantId, string personId, int version, CancellationToken cancellationToken = default)
    {
        if (_byPerson.TryGetValue((tenantId, personId), out var versions) && versions.TryGetValue(version, out var master))
        {
            return Task.FromResult<PhotoMaster?>(master);
        }

        return Task.FromResult<PhotoMaster?>(null);
    }

    public Task<IReadOnlyList<PhotoMaster>> ListVersionsAsync(string tenantId, string personId, CancellationToken cancellationToken = default)
    {
        if (!_byPerson.TryGetValue((tenantId, personId), out var versions))
        {
            return Task.FromResult<IReadOnlyList<PhotoMaster>>([]);
        }

        return Task.FromResult<IReadOnlyList<PhotoMaster>>(versions.Values.OrderBy(m => m.Version).ToList());
    }
}

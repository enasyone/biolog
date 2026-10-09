using System.Collections.Concurrent;

namespace Biolog.Features.Photography.Domain;

/// <summary>In-memory placeholder for <see cref="IPhotoDerivativeRepository"/> - same ADR-0005 rationale as <see cref="InMemoryPhotoMasterRepository"/>.</summary>
internal sealed class InMemoryPhotoDerivativeRepository : IPhotoDerivativeRepository
{
    private readonly ConcurrentDictionary<(string TenantId, string PersonId, int MasterVersion, DerivativeKind Kind), PhotoDerivative> _byKey = new();

    public Task<PhotoDerivative> UpsertAsync(PhotoDerivative derivative, CancellationToken cancellationToken = default)
    {
        _byKey[(derivative.TenantId, derivative.PersonId, derivative.MasterVersion, derivative.Kind)] = derivative;
        return Task.FromResult(derivative);
    }

    public Task<PhotoDerivative?> GetAsync(string tenantId, string personId, int masterVersion, DerivativeKind kind, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_byKey.TryGetValue((tenantId, personId, masterVersion, kind), out var derivative) ? derivative : null);
    }

    public Task<IReadOnlyList<PhotoDerivative>> ListForMasterAsync(string tenantId, string personId, int masterVersion, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<PhotoDerivative> matches = _byKey.Values
            .Where(d => d.TenantId == tenantId && d.PersonId == personId && d.MasterVersion == masterVersion)
            .OrderBy(d => d.Kind)
            .ToList();
        return Task.FromResult(matches);
    }
}

namespace Biolog.Features.Photography.Domain;

/// <summary>
/// Storage for <see cref="PhotoDerivative"/>, keyed by <c>(PersonId, MasterVersion, Kind)</c>.
///
/// <b>This interface's own shape is BIO-PHO-001-AC03's structural proof.</b> It exposes
/// exactly one write method, <see cref="UpsertAsync"/>, and it is typed to accept only a
/// <see cref="PhotoDerivative"/> - there is no method here, and no reference anywhere in this
/// class to <see cref="IPhotoMasterRepository"/>'s write side, through which a derivative
/// regeneration could reach (let alone replace) a master row even by accident. AC03 therefore
/// does not depend on <see cref="PhotoDerivativeService"/> remembering to be careful; it is
/// unreachable by construction.
///
/// <b><c>internal</c>, from the start</b> - same rationale as <see cref="IPhotoMasterRepository"/>.
/// </summary>
internal interface IPhotoDerivativeRepository
{
    /// <summary>Replaces any prior derivative at this exact key. This is what makes "regeneration" a real operation - each call supersedes the previous rendition of the same kind for the same master version.</summary>
    Task<PhotoDerivative> UpsertAsync(PhotoDerivative derivative, CancellationToken cancellationToken = default);

    Task<PhotoDerivative?> GetAsync(string tenantId, string personId, int masterVersion, DerivativeKind kind, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PhotoDerivative>> ListForMasterAsync(string tenantId, string personId, int masterVersion, CancellationToken cancellationToken = default);
}

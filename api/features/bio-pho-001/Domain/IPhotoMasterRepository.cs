namespace Biolog.Features.Photography.Domain;

/// <summary>
/// Storage for <see cref="PhotoMaster"/>, keyed by <c>(PersonId, Version)</c>. Deliberately has
/// NO update/replace/delete method of any kind - AC01's immutability is enforced structurally
/// here, not merely by convention: there is no method on this interface a caller could even
/// attempt to use to mutate or remove a previously appended row, mirroring
/// <c>IPersonProfileRepository</c>'s identical "no delete method" enforcement for a different
/// invariant.
///
/// <b><c>internal</c>, from the start.</b> A public repository plus a public aggregate
/// constructor would let any code that can reference this assembly bypass
/// <see cref="PhotoMasterService"/> entirely - skip the tenant boundary, the next-version
/// computation and the audit trail. This interface, its implementation, and
/// <see cref="PhotoMaster"/>'s constructor are all <c>internal</c>; <c>InternalsVisibleTo</c>
/// in this project's <c>.csproj</c> grants the same access to <c>Biolog.Api</c> (DI
/// registration) and <c>Biolog.Features.Photography.Tests</c>.
///
/// Every read and write method takes <c>tenantId</c> explicitly and asserts a matching
/// record's own <see cref="PhotoMaster.TenantId"/> before returning it - the same
/// belt-and-braces rule every sibling tenant-plane story's repository applies.
/// </summary>
internal interface IPhotoMasterRepository
{
    /// <summary>Throws <see cref="PhotoError.MasterVersionAlreadyExists"/> if this exact (PersonId, Version) is already recorded - belt-and-braces; the caller is expected to have already computed a fresh version under <see cref="PhotoMasterLockRegistry"/>.</summary>
    Task<PhotoMaster> AppendAsync(PhotoMaster master, CancellationToken cancellationToken = default);

    Task<PhotoMaster?> GetLatestAsync(string tenantId, string personId, CancellationToken cancellationToken = default);

    Task<PhotoMaster?> GetVersionAsync(string tenantId, string personId, int version, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PhotoMaster>> ListVersionsAsync(string tenantId, string personId, CancellationToken cancellationToken = default);
}

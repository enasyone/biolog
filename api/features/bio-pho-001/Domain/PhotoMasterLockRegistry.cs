using System.Collections.Concurrent;

namespace Biolog.Features.Photography.Domain;

/// <summary>
/// Per-person-id in-process locking for <see cref="PhotoMasterService.AppendAsync"/> AND
/// <see cref="PhotoDerivativeService.RegenerateAsync"/> - one shared table across both, not two
/// separate registries, because both operations serialize on the same actual invariant for a
/// given person: "compute the next fact from this person's current master state without a
/// concurrent append changing it underneath you." A derivative regeneration reading a stale
/// master mid-append is the same class of race as two concurrent appends racing on the next
/// version number.
///
/// Held in its OWN singleton class rather than as instance state on either service - unlike
/// <c>TenantService</c> (which owns its lock table directly and is registered Singleton), both
/// services here read a per-request <see cref="Tenancy.ICurrentTenantProvider"/>, so they MUST
/// be registered Scoped - a Singleton cannot safely capture a Scoped dependency. Splitting the
/// lock table into this separate Singleton class, injected into both Scoped services, is the
/// same shape <c>PersonLockRegistry</c> (BIO-IDN-001) already uses for the identical reason.
/// </summary>
public sealed class PhotoMasterLockRegistry
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    public async Task<IDisposable> AcquireAsync(string personId, CancellationToken cancellationToken)
    {
        var gate = _locks.GetOrAdd(personId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        return new Releaser(gate);
    }

    private sealed class Releaser(SemaphoreSlim gate) : IDisposable
    {
        public void Dispose() => gate.Release();
    }
}

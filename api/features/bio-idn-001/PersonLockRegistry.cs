using System.Collections.Concurrent;

namespace Biolog.Features.PersonRegistry;

/// <summary>
/// Per-person-id in-process locking, held in its OWN singleton class rather than as instance
/// state on <see cref="PersonService"/> itself - unlike <c>TenantService</c>/
/// <c>TenantDomainService</c> (which own their lock table directly and are registered
/// Singleton, since control-plane work consumes no scoped dependency), <see cref="PersonService"/>
/// reads a per-request <see cref="Tenancy.ICurrentTenantProvider"/>, so it MUST be registered
/// Scoped - a Singleton cannot safely capture a Scoped dependency (a classic ASP.NET Core
/// captive-dependency defect: it would pin the first request's tenant context forever).
/// Splitting the lock table into this separate Singleton class, injected into the Scoped
/// <see cref="PersonService"/>, is the same shape <c>DeviceLockRegistry</c> (BIO-DEV-001)
/// already uses for the identical reason - one shared lock table across every request, a
/// fresh <see cref="PersonService"/> per request.
/// </summary>
public sealed class PersonLockRegistry
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

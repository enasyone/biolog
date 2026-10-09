using System.Collections.Concurrent;

namespace Biolog.Features.PersonProfileRegistry;

/// <summary>
/// Per-<c>(PersonId, Type)</c> in-process locking, held in its OWN singleton class rather than
/// as instance state on <see cref="PersonProfileService"/> - identical reasoning to
/// BIO-IDN-001's <c>PersonLockRegistry</c>: <see cref="PersonProfileService"/> reads a
/// per-request <see cref="Tenancy.ICurrentTenantProvider"/>, so it must be registered Scoped,
/// and a Singleton cannot safely capture a Scoped dependency.
/// </summary>
public sealed class PersonProfileLockRegistry
{
    private readonly ConcurrentDictionary<(string PersonId, Domain.ProfileType Type), SemaphoreSlim> _locks = new();

    public async Task<IDisposable> AcquireAsync(string personId, Domain.ProfileType type, CancellationToken cancellationToken)
    {
        var gate = _locks.GetOrAdd((personId, type), static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        return new Releaser(gate);
    }

    private sealed class Releaser(SemaphoreSlim gate) : IDisposable
    {
        public void Dispose() => gate.Release();
    }
}

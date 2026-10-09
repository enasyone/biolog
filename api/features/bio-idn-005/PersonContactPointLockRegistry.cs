using System.Collections.Concurrent;

namespace Biolog.Features.DuplicateReview;

/// <summary>
/// Per-<c>(TenantId, PersonId)</c> in-process locking for <see cref="PersonContactPointService"/>,
/// held in its own singleton class - identical reasoning to
/// <see cref="DuplicateCandidateLockRegistry"/> and every sibling story's own lock registry:
/// the service reads a per-request <see cref="Tenancy.ICurrentTenantProvider"/>, so it must be
/// registered Scoped, and a Singleton cannot safely capture a Scoped dependency. A separate
/// registry from <see cref="DuplicateCandidateLockRegistry"/> because the two aggregates have
/// different natural keys (a pair vs. a single person) and no invariant spans both.
/// </summary>
public sealed class PersonContactPointLockRegistry
{
    private readonly ConcurrentDictionary<(string TenantId, string PersonId), SemaphoreSlim> _locks = new();

    public async Task<IDisposable> AcquireAsync(string tenantId, string personId, CancellationToken cancellationToken)
    {
        var gate = _locks.GetOrAdd((tenantId, personId), static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        return new Releaser(gate);
    }

    private sealed class Releaser(SemaphoreSlim gate) : IDisposable
    {
        public void Dispose() => gate.Release();
    }
}

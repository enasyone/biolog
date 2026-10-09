using System.Collections.Concurrent;

namespace Biolog.Features.PersonLifecycle;

/// <summary>
/// Per-<c>PersonId</c> in-process locking, held in its OWN singleton class rather than as
/// instance state on <see cref="PersonLifecycleService"/> - identical reasoning to every sibling
/// story's lock registry: <see cref="PersonLifecycleService"/> reads a per-request
/// <see cref="Tenancy.ICurrentTenantProvider"/>, so it must be registered Scoped, and a
/// Singleton cannot safely capture a Scoped dependency.
///
/// <b>What this lock does NOT provide, disclosed honestly.</b> This registry only serializes
/// concurrent callers going THROUGH this feature's own <see cref="PersonLifecycleService"/>. It
/// cannot serialize against a hypothetical concurrent write to the SAME person from BIO-IDN-001's
/// own endpoints directly, or from any other feature that also holds a write-capable gateway
/// onto PERSON - the real source-of-truth safety for the underlying status field is
/// BIO-IDN-001's own per-person lock inside <c>PersonService.UpdateStatusAsync</c>, which this
/// project has no visibility into or control over. This lock's purpose is narrower and still
/// real: it prevents two concurrent BIO-IDN-004 callers from both passing this feature's own
/// pre-checks and both writing this feature's own supplementary audit/revocation-event records
/// for what might resolve to only one successful underlying change.
/// </summary>
public sealed class PersonLifecycleLockRegistry
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

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

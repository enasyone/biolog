using System.Collections.Concurrent;

namespace Biolog.Features.Photography.Domain;

/// <summary>
/// Per-person-id in-process locking for <see cref="PhotoAssignmentService.AssignAsync"/> -
/// its own registry, separate from <see cref="PhotoMasterLockRegistry"/>, because an assignment
/// read-compare-and-set is a different invariant from "compute the next master/derivative
/// fact" and the two must not serialize on each other (a slow assignment CAS retry should never
/// block an unrelated master upload for the same person, or vice versa). Same registered
/// lifetime rationale as <see cref="PhotoMasterLockRegistry"/>'s header.
/// </summary>
public sealed class PhotoAssignmentLockRegistry
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

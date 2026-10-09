using System.Collections.Concurrent;

namespace Biolog.Features.PhotoBooth.Domain;

/// <summary>
/// Per-person-id in-process locking for <see cref="CaptureService.SubmitAsync"/> (computing the
/// next sequence number) AND <see cref="CaptureService.DecideAsync"/> (the approve/reject
/// compare-and-set) - one shared table across both, mirroring <c>PhotoMasterLockRegistry</c>'s
/// (BIO-PHO-001) identical reasoning: both operations serialize on the same actual invariant for
/// a given person, so splitting them into two registries would buy nothing.
///
/// Held in its own injected Singleton, separate from the Scoped <see cref="CaptureService"/> -
/// same registration-lifetime rationale as every sibling story's own lock registry.
/// </summary>
public sealed class BoothCaptureLockRegistry
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

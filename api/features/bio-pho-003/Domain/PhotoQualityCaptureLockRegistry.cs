using System.Collections.Concurrent;

namespace Biolog.Features.PhotoQuality.Domain;

/// <summary>
/// Per-person-id in-process locking for <see cref="PhotoQualityService.SubmitAsync"/> (computing
/// the next sequence number) AND <see cref="PhotoQualityService.ApproveAsync"/>/
/// <see cref="PhotoQualityService.RejectAsync"/> (the operator-decision compare-and-set) - one
/// shared table across both, mirroring <c>BoothCaptureLockRegistry</c>'s (BIO-PHO-002) identical
/// reasoning: both operations serialize on the same actual invariant for a given person, so
/// splitting them into two registries would buy nothing.
///
/// Held in its own injected Singleton, separate from the Scoped <see cref="PhotoQualityService"/>
/// - same registration-lifetime rationale as every sibling story's own lock registry.
/// </summary>
public sealed class PhotoQualityCaptureLockRegistry
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

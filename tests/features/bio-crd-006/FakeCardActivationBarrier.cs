// Developer tests for BIO-CRD-006. SYNTHETIC fixtures only.
using Biolog.Features.CardLifecycle.Domain;

namespace Biolog.Features.CardLifecycle.Tests;

/// <summary>A real, working in-process lock (same shape as the production <c>CardActivationBarrierLockRegistry</c> it stands in for) - this feature's own tests never need the REAL cross-feature singleton, only genuine per-key serialization.</summary>
public sealed class FakeCardActivationBarrier : ICardActivationBarrier
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    public async Task<IDisposable> AcquireAsync(string tenantId, string personId, string cardClass, int sequenceNumber, CancellationToken cancellationToken)
    {
        var key = $"{tenantId}\u0000{personId}\u0000{cardClass}\u0000{sequenceNumber}";
        var gate = _locks.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        return new Releaser(gate);
    }

    private sealed class Releaser(SemaphoreSlim gate) : IDisposable
    {
        public void Dispose() => gate.Release();
    }
}

using System.Collections.Concurrent;

namespace Biolog.Features.BiometricRetention.Domain;

/// <summary>Per-record in-process locking (ADR-0003 Decision 7, element 3), held in its own
/// singleton class - same shape as BIO-BIO-002's own <c>FingerprintEnrolmentLockRegistry</c>.
/// Keyed by a plain string the caller composes (tenant + kind + record id), since this registry
/// guards two distinct record kinds sharing one lock space would never otherwise collide
/// on.</summary>
internal sealed class BiometricRetentionLockRegistry
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    public async Task<IDisposable> AcquireAsync(string key, CancellationToken cancellationToken)
    {
        var gate = _locks.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        return new Releaser(gate);
    }

    private sealed class Releaser(SemaphoreSlim gate) : IDisposable
    {
        public void Dispose() => gate.Release();
    }
}

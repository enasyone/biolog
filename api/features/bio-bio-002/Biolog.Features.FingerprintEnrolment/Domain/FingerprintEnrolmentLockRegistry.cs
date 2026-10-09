using System.Collections.Concurrent;

namespace Biolog.Features.FingerprintEnrolment.Domain;

/// <summary>Per-session-id in-process locking, held in its own singleton class so
/// <see cref="Service.FingerprintEnrolmentService"/> can be registered independently of request
/// scope - same shape as BIO-IDN-001's <c>PersonLockRegistry</c>/BIO-DEV-001's
/// <c>DeviceLockRegistry</c>.</summary>
public sealed class FingerprintEnrolmentLockRegistry
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    public async Task<IDisposable> AcquireAsync(string sessionId, CancellationToken cancellationToken)
    {
        var gate = _locks.GetOrAdd(sessionId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        return new Releaser(gate);
    }

    private sealed class Releaser(SemaphoreSlim gate) : IDisposable
    {
        public void Dispose() => gate.Release();
    }
}

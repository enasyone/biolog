using System.Collections.Concurrent;

namespace Biolog.Api.Features.DeviceCertification;

/// <summary>
/// Per-certification-entry in-process locking, mirroring
/// <see cref="Features.DeviceRegistry.DeviceLockRegistry"/> exactly - the identical
/// reasoning applies here: auditing before the repository commit alone does not close the
/// "committed but never audited" defect if a second writer for the SAME entry can race
/// between this service's pre-check and the repository's own version check
/// (BIO-DEV-001-QA-02, ADR-0003 Decision 7). The lock key already encodes tenant scope
/// (see <see cref="DeviceCertificationService"/>'s internal key), so a tenant's own
/// override and the platform-wide default for the identical vendor+model+firmware+sdk
/// combination serialize independently - they are different aggregates. Registered
/// singleton, unlike the scoped <see cref="DeviceCertificationService"/>, so the same lock
/// is shared across every request.
/// </summary>
public sealed class DeviceCertificationLockRegistry
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public async Task<IDisposable> AcquireAsync(string lockKey, CancellationToken cancellationToken)
    {
        var semaphore = _locks.GetOrAdd(lockKey, static _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken);
        return new Releaser(semaphore);
    }

    private sealed class Releaser(SemaphoreSlim semaphore) : IDisposable
    {
        public void Dispose() => semaphore.Release();
    }
}

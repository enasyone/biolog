using System.Collections.Concurrent;

namespace Biolog.Api.Features.DeviceRegistry;

/// <summary>
/// Per-device in-process locking (BIO-DEV-001-QA-02, corrected a second time). Auditing
/// before the repository commit closes the "committed but never audited" defect, but not
/// by itself: <see cref="InMemoryDeviceRepository.UpdateStatusAsync"/> re-checks the
/// device's version independently, at its own call time, separate from
/// <see cref="DeviceService"/>'s earlier pre-check. A second writer for the SAME device can
/// still change the version in between the two, so the eventual repository call rejects a
/// mutation whose audit event has ALREADY durably recorded <c>Outcome: Allowed</c> - a false
/// success claim, not an "honest attempt" record, regardless of how narrow the window is
/// (Kemi/Seyi's finding on the identical shape in BIO-TEN-001).
///
/// This registry serializes the whole pre-check-audit-commit sequence per device id, so no
/// second writer for the same device can even begin its own pre-check until the first
/// writer's audit AND commit have both resolved - removing the race by construction rather
/// than narrowing its window. Registered singleton, unlike the scoped
/// <see cref="DeviceService"/>, so the same lock is shared across every request.
/// </summary>
public sealed class DeviceLockRegistry
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public async Task<IDisposable> AcquireAsync(string deviceId, CancellationToken cancellationToken)
    {
        var semaphore = _locks.GetOrAdd(deviceId, static _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken);
        return new Releaser(semaphore);
    }

    private sealed class Releaser(SemaphoreSlim semaphore) : IDisposable
    {
        public void Dispose() => semaphore.Release();
    }
}

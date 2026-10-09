using System.Collections.Concurrent;

namespace Biolog.Features.WorkforceAttendance;

/// <summary>Per-<c>(TenantId, ShiftId)</c> locking (AC01) - guards one shift's own compare-and-set read-modify-write against a concurrent second writer. Registered Singleton so the lock table outlives any one request, the same captive-dependency-safe split every sibling story in this codebase applies.</summary>
public sealed class WorkforceShiftLockRegistry
{
    private readonly ConcurrentDictionary<(string TenantId, string ShiftId), SemaphoreSlim> _locks = new();

    public async Task<IDisposable> AcquireAsync(string tenantId, string shiftId, CancellationToken cancellationToken)
    {
        var gate = _locks.GetOrAdd((tenantId, shiftId), static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        return new Releaser(gate);
    }

    private sealed class Releaser(SemaphoreSlim gate) : IDisposable
    {
        public void Dispose() => gate.Release();
    }
}

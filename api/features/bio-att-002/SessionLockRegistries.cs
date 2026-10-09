using System.Collections.Concurrent;

namespace Biolog.Features.AttendanceSessions;

/// <summary>Per-<c>(TenantId, SessionId)</c> in-process locking for roster updates (AC01) - guards the compare-and-set read-modify-write against a concurrent second writer, the same shape BIO-ATT-001's own lock registries use.</summary>
public sealed class SessionLockRegistry
{
    private readonly ConcurrentDictionary<(string TenantId, string SessionId), SemaphoreSlim> _locks = new();

    public async Task<IDisposable> AcquireAsync(string tenantId, string sessionId, CancellationToken cancellationToken)
    {
        var gate = _locks.GetOrAdd((tenantId, sessionId), static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        return new Releaser(gate);
    }

    private sealed class Releaser(SemaphoreSlim gate) : IDisposable
    {
        public void Dispose() => gate.Release();
    }
}

/// <summary>Per-<c>(TenantId, SessionType)</c> locking for a <see cref="Domain.SessionAttendancePolicy"/> (AC02) - guards the compare-and-set read-modify-write against a concurrent second writer.</summary>
public sealed class SessionAttendancePolicyLockRegistry
{
    private readonly ConcurrentDictionary<(string TenantId, Domain.SessionType SessionType), SemaphoreSlim> _locks = new();

    public async Task<IDisposable> AcquireAsync(string tenantId, Domain.SessionType sessionType, CancellationToken cancellationToken)
    {
        var gate = _locks.GetOrAdd((tenantId, sessionType), static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        return new Releaser(gate);
    }

    private sealed class Releaser(SemaphoreSlim gate) : IDisposable
    {
        public void Dispose() => gate.Release();
    }
}

using System.Collections.Concurrent;

namespace Biolog.Features.Visitors;

/// <summary>
/// Per-<c>(TenantId, VisitorId)</c> in-process locking for a single visitor record's mutations
/// (ADR-0003 Decision 7's audit-atomicity pattern). Registered Singleton so the lock table
/// outlives any one request; <see cref="VisitorRecordService"/> itself stays Scoped (it reads a
/// per-request <see cref="Tenancy.ICurrentTenantProvider"/>).
/// </summary>
public sealed class VisitorRecordLockRegistry
{
    private readonly ConcurrentDictionary<(string TenantId, string VisitorId), SemaphoreSlim> _locks = new();

    public async Task<IDisposable> AcquireAsync(string tenantId, string visitorId, CancellationToken cancellationToken)
    {
        var gate = _locks.GetOrAdd((tenantId, visitorId), static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        return new Releaser(gate);
    }

    private sealed class Releaser(SemaphoreSlim gate) : IDisposable
    {
        public void Dispose() => gate.Release();
    }
}

/// <summary>Per-tenant locking for the single tenant-wide <see cref="Domain.VisitorTenantConfig"/> (AC03) - guards the compare-and-set read-modify-write against a concurrent second writer.</summary>
public sealed class VisitorConfigLockRegistry
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public async Task<IDisposable> AcquireAsync(string tenantId, CancellationToken cancellationToken)
    {
        var gate = _locks.GetOrAdd(tenantId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        return new Releaser(gate);
    }

    private sealed class Releaser(SemaphoreSlim gate) : IDisposable
    {
        public void Dispose() => gate.Release();
    }
}

using System.Collections.Concurrent;

namespace Biolog.Features.Attendance;

/// <summary>
/// Per-<c>(TenantId, IdempotencyKey)</c> in-process locking for event capture (AC03) - two
/// concurrent requests presenting the SAME idempotency key must not both observe "no prior
/// event" and both insert, which would defeat idempotency under a race exactly the way
/// BIO-IDN-003-QA-01 proved for tenant-wide uniqueness. Registered Singleton so the lock table
/// outlives any one request; <see cref="AttendanceEventService"/> itself stays Scoped (it reads
/// a per-request <see cref="Tenancy.ICurrentTenantProvider"/>), the same captive-dependency-safe
/// split every sibling story in this codebase already applies.
/// </summary>
public sealed class AttendanceEventLockRegistry
{
    private readonly ConcurrentDictionary<(string TenantId, string IdempotencyKey), SemaphoreSlim> _locks = new();

    public async Task<IDisposable> AcquireAsync(string tenantId, string idempotencyKey, CancellationToken cancellationToken)
    {
        var gate = _locks.GetOrAdd((tenantId, idempotencyKey), static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        return new Releaser(gate);
    }

    private sealed class Releaser(SemaphoreSlim gate) : IDisposable
    {
        public void Dispose() => gate.Release();
    }
}

/// <summary>Per-tenant locking for the single tenant-wide <see cref="Domain.AttendancePolicy"/> (AC04) - guards the compare-and-set read-modify-write against a concurrent second writer.</summary>
public sealed class AttendancePolicyLockRegistry
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

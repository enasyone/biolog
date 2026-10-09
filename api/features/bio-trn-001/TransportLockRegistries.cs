using System.Collections.Concurrent;

namespace Biolog.Features.Transport;

/// <summary>
/// Per-<c>(TenantId, RecordId)</c> in-process locking for each of this feature's four aggregates
/// (ADR-0003 Decision 7's audit-atomicity pattern), plus
/// <see cref="PersonAssignmentUniquenessLockRegistry"/> for AC02's tenant/person-scoped overlap
/// invariant. Five small, near-identical registries kept in one file (a deliberate consolidation
/// for this story's four-aggregate scope, documented here rather than five near-empty files) -
/// each Singleton so its lock table outlives any one request, mirroring BIO-VIS-001's own
/// <c>VisitorLockRegistries.cs</c> (which held two).
/// </summary>
public sealed class RouteLockRegistry
{
    private readonly ConcurrentDictionary<(string TenantId, string RouteId), SemaphoreSlim> _locks = new();

    public async Task<IDisposable> AcquireAsync(string tenantId, string routeId, CancellationToken cancellationToken)
    {
        var gate = _locks.GetOrAdd((tenantId, routeId), static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        return new Releaser(gate);
    }

    private sealed class Releaser(SemaphoreSlim gate) : IDisposable
    {
        public void Dispose() => gate.Release();
    }
}

public sealed class VehicleLockRegistry
{
    private readonly ConcurrentDictionary<(string TenantId, string VehicleId), SemaphoreSlim> _locks = new();

    public async Task<IDisposable> AcquireAsync(string tenantId, string vehicleId, CancellationToken cancellationToken)
    {
        var gate = _locks.GetOrAdd((tenantId, vehicleId), static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        return new Releaser(gate);
    }

    private sealed class Releaser(SemaphoreSlim gate) : IDisposable
    {
        public void Dispose() => gate.Release();
    }
}

public sealed class StopLockRegistry
{
    private readonly ConcurrentDictionary<(string TenantId, string StopId), SemaphoreSlim> _locks = new();

    public async Task<IDisposable> AcquireAsync(string tenantId, string stopId, CancellationToken cancellationToken)
    {
        var gate = _locks.GetOrAdd((tenantId, stopId), static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        return new Releaser(gate);
    }

    private sealed class Releaser(SemaphoreSlim gate) : IDisposable
    {
        public void Dispose() => gate.Release();
    }
}

public sealed class PersonAssignmentLockRegistry
{
    private readonly ConcurrentDictionary<(string TenantId, string AssignmentId), SemaphoreSlim> _locks = new();

    public async Task<IDisposable> AcquireAsync(string tenantId, string assignmentId, CancellationToken cancellationToken)
    {
        var gate = _locks.GetOrAdd((tenantId, assignmentId), static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        return new Releaser(gate);
    }

    private sealed class Releaser(SemaphoreSlim gate) : IDisposable
    {
        public void Dispose() => gate.Release();
    }
}

/// <summary>
/// AC02's overlap invariant is scoped to <c>(TenantId, PersonId)</c> across MULTIPLE assignment
/// records, not any single record - a per-record lock alone cannot protect it (the exact lesson
/// BIO-IDN-003's own identifier-uniqueness rule already established: lock scope must match
/// invariant scope). <c>PersonAssignmentService.CreateAsync</c> acquires THIS lock first, checks
/// the overlap under it, then proceeds - re-applying that precedent rather than rediscovering it.
/// </summary>
public sealed class PersonAssignmentUniquenessLockRegistry
{
    private readonly ConcurrentDictionary<(string TenantId, string PersonId), SemaphoreSlim> _locks = new();

    public async Task<IDisposable> AcquireAsync(string tenantId, string personId, CancellationToken cancellationToken)
    {
        var gate = _locks.GetOrAdd((tenantId, personId), static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        return new Releaser(gate);
    }

    private sealed class Releaser(SemaphoreSlim gate) : IDisposable
    {
        public void Dispose() => gate.Release();
    }
}

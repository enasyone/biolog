using System.Collections.Concurrent;

namespace Biolog.Features.TransportEvents;

/// <summary>
/// Per-<c>(TenantId, IdempotencyKey)</c> locking for AC02's idempotent capture (ADR-0003 Decision
/// 7's audit-atomicity pattern), plus a per-tenant lock for <see cref="TransportNotificationConfig"/>'s
/// own CAS writes. Mirrors BIO-ATT-001's own <c>AttendanceEventLockRegistry</c> and BIO-TRN-001's
/// own <c>TransportLockRegistries.cs</c> consolidation shape.
/// </summary>
public sealed class TransportEventLockRegistry
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

public sealed class TransportNotificationConfigLockRegistry
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

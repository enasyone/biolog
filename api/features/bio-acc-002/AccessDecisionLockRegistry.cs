using System.Collections.Concurrent;

namespace Biolog.Features.AccessDecisions;

/// <summary>Per-<c>(TenantId, IdempotencyKey)</c> locking for AC02/AC04's idempotent decision requests (ADR-0003 Decision 7's audit-atomicity pattern) - mirrors BIO-ATT-001's/BIO-TRN-002's own lock registry shape.</summary>
public sealed class AccessDecisionLockRegistry
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

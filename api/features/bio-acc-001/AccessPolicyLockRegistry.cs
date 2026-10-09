using System.Collections.Concurrent;

namespace Biolog.Features.AccessPolicy;

/// <summary>
/// Per-<c>(TenantId, PolicyKey)</c> in-process locking, held in its own singleton class -
/// identical reasoning to every sibling story's own lock registry: <see cref="AccessPolicyService"/>
/// reads a per-request <see cref="Tenancy.ICurrentTenantProvider"/>, so it must be registered
/// Scoped, and a Singleton cannot safely capture a Scoped dependency.
/// </summary>
public sealed class AccessPolicyLockRegistry
{
    private readonly ConcurrentDictionary<(string TenantId, string PolicyKey), SemaphoreSlim> _locks = new();

    public async Task<IDisposable> AcquireAsync(string tenantId, string policyKey, CancellationToken cancellationToken)
    {
        var gate = _locks.GetOrAdd((tenantId, policyKey), static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        return new Releaser(gate);
    }

    private sealed class Releaser(SemaphoreSlim gate) : IDisposable
    {
        public void Dispose() => gate.Release();
    }
}

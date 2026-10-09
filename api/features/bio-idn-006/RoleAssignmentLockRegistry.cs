using System.Collections.Concurrent;
using Biolog.Features.Rbac.Domain;

namespace Biolog.Features.Rbac;

/// <summary>
/// Per-<c>(TenantId, PersonId, Role)</c> in-process locking, held in its own singleton class -
/// identical reasoning to every sibling story's own lock registry: <see cref="RoleAssignmentService"/>
/// reads a per-request <see cref="Tenancy.ICurrentTenantProvider"/>, so it must be registered
/// Scoped, and a Singleton cannot safely capture a Scoped dependency.
/// </summary>
public sealed class RoleAssignmentLockRegistry
{
    private readonly ConcurrentDictionary<(string TenantId, string PersonId, RoleType Role), SemaphoreSlim> _locks = new();

    public async Task<IDisposable> AcquireAsync(string tenantId, string personId, RoleType role, CancellationToken cancellationToken)
    {
        var gate = _locks.GetOrAdd((tenantId, personId, role), static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        return new Releaser(gate);
    }

    private sealed class Releaser(SemaphoreSlim gate) : IDisposable
    {
        public void Dispose() => gate.Release();
    }
}

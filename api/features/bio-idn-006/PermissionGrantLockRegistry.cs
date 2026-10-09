using System.Collections.Concurrent;
using Biolog.Features.Rbac.Domain;

namespace Biolog.Features.Rbac;

/// <summary>Per-<c>(TenantId, Role, PermissionKey)</c> in-process locking for <see cref="PermissionService"/>'s grant writes. See <see cref="RoleAssignmentLockRegistry"/> for the captive-dependency rationale.</summary>
public sealed class PermissionGrantLockRegistry
{
    private readonly ConcurrentDictionary<(string TenantId, RoleType Role, string PermissionKey), SemaphoreSlim> _locks = new();

    public async Task<IDisposable> AcquireAsync(string tenantId, RoleType role, string permissionKey, CancellationToken cancellationToken)
    {
        var gate = _locks.GetOrAdd((tenantId, role, permissionKey), static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        return new Releaser(gate);
    }

    private sealed class Releaser(SemaphoreSlim gate) : IDisposable
    {
        public void Dispose() => gate.Release();
    }
}

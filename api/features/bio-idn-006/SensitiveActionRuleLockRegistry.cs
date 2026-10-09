using System.Collections.Concurrent;

namespace Biolog.Features.Rbac;

/// <summary>Per-<c>(TenantId, ActionKey)</c> in-process locking for <see cref="MakerCheckerService"/>'s rule writes. See <see cref="RoleAssignmentLockRegistry"/> for the captive-dependency rationale.</summary>
public sealed class SensitiveActionRuleLockRegistry
{
    private readonly ConcurrentDictionary<(string TenantId, string ActionKey), SemaphoreSlim> _locks = new();

    public async Task<IDisposable> AcquireAsync(string tenantId, string actionKey, CancellationToken cancellationToken)
    {
        var gate = _locks.GetOrAdd((tenantId, actionKey), static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        return new Releaser(gate);
    }

    private sealed class Releaser(SemaphoreSlim gate) : IDisposable
    {
        public void Dispose() => gate.Release();
    }
}

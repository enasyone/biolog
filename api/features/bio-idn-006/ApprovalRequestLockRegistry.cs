using System.Collections.Concurrent;

namespace Biolog.Features.Rbac;

/// <summary>
/// Per-<c>(TenantId, ActionKey, TargetRef)</c> in-process locking for
/// <see cref="MakerCheckerService"/>'s approval-request writes - the same scope
/// <see cref="ApprovalRequest"/>'s own "at most one Pending per target" invariant spans, mirroring
/// BIO-IDN-003's <c>InstitutionalIdentifierLockRegistry</c> shape exactly (natural key +
/// history). See <see cref="RoleAssignmentLockRegistry"/> for the captive-dependency rationale.
/// </summary>
public sealed class ApprovalRequestLockRegistry
{
    private readonly ConcurrentDictionary<(string TenantId, string ActionKey, string TargetRef), SemaphoreSlim> _locks = new();

    public async Task<IDisposable> AcquireAsync(string tenantId, string actionKey, string targetRef, CancellationToken cancellationToken)
    {
        var gate = _locks.GetOrAdd((tenantId, actionKey, targetRef), static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        return new Releaser(gate);
    }

    private sealed class Releaser(SemaphoreSlim gate) : IDisposable
    {
        public void Dispose() => gate.Release();
    }
}

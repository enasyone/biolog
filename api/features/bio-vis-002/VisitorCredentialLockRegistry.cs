using System.Collections.Concurrent;

namespace Biolog.Features.VisitorCredentials;

/// <summary>
/// Per-<c>(TenantId, VisitorRecordId)</c> in-process locking for a visitor's credential mutations
/// (ADR-0003 Decision 7's audit-atomicity pattern) - keyed by the VISITOR, not the credential id
/// itself, so a grant attempt and a concurrent revoke/checkout for the SAME visitor can never
/// race each other into two simultaneously-Granted credentials. Registered Singleton so the lock
/// table outlives any one request; <see cref="VisitorCredentialService"/> itself stays Scoped (it
/// reads a per-request <see cref="Tenancy.ICurrentTenantProvider"/>).
/// </summary>
public sealed class VisitorCredentialLockRegistry
{
    private readonly ConcurrentDictionary<(string TenantId, string VisitorRecordId), SemaphoreSlim> _locks = new();

    public async Task<IDisposable> AcquireAsync(string tenantId, string visitorRecordId, CancellationToken cancellationToken)
    {
        var gate = _locks.GetOrAdd((tenantId, visitorRecordId), static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        return new Releaser(gate);
    }

    private sealed class Releaser(SemaphoreSlim gate) : IDisposable
    {
        public void Dispose() => gate.Release();
    }
}

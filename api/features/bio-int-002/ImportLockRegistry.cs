using System.Collections.Concurrent;

namespace Biolog.Features.Integrations;

/// <summary>
/// Per-<c>(TenantId, MappingId)</c> in-process locking (ADR-0003 Decision 7's audit-atomicity
/// pattern) - keyed by the MAPPING, so a config edit and a concurrent apply/preview run against
/// the SAME mapping can never race each other. Registered Singleton so the lock table outlives
/// any one request; the services themselves stay Scoped.
/// </summary>
public sealed class ImportLockRegistry
{
    private readonly ConcurrentDictionary<(string TenantId, string MappingId), SemaphoreSlim> _locks = new();

    public async Task<IDisposable> AcquireAsync(string tenantId, string mappingId, CancellationToken cancellationToken)
    {
        var gate = _locks.GetOrAdd((tenantId, mappingId), static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        return new Releaser(gate);
    }

    private sealed class Releaser(SemaphoreSlim gate) : IDisposable
    {
        public void Dispose() => gate.Release();
    }
}

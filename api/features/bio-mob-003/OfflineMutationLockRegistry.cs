using System.Collections.Concurrent;

namespace Biolog.Features.OfflineSync;

/// <summary>
/// Per-(tenant, aggregate) in-process locking, held in its own singleton class rather than as
/// instance state on <see cref="OfflineMutationIntakeService"/> itself - the same split
/// <c>CardIssueLockRegistry</c> (BIO-CRD-005) uses for the identical reason:
/// <see cref="OfflineMutationIntakeService"/> is registered Scoped, and a Singleton cannot
/// safely capture a Scoped dependency.
///
/// Serialising every mutation against its own <c>(tenant_id, aggregate_id)</c> is what makes
/// <see cref="OfflineMutationIntakeService.IntakeAsync"/>'s peek-version -> audit -> commit-version
/// sequence genuinely race-free (BIO-MOB-003 round 4, Seyi): nothing else can observe or commit
/// a version for the same aggregate while one request's audit write is still in flight, so there
/// is never a window where the ledger has advanced ahead of a not-yet-confirmed audit record.
/// </summary>
public sealed class OfflineMutationLockRegistry
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    public async Task<IDisposable> AcquireAsync(string tenantId, string aggregateId, CancellationToken cancellationToken)
    {
        var key = $"{tenantId}\u0000{aggregateId}";
        var gate = _locks.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        return new Releaser(gate);
    }

    private sealed class Releaser(SemaphoreSlim gate) : IDisposable
    {
        public void Dispose() => gate.Release();
    }
}

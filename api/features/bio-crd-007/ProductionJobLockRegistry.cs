using System.Collections.Concurrent;

namespace Biolog.Features.CardProduction;

/// <summary>
/// Per-(tenant, person, card class, sequence number) in-process locking - a Singleton, not
/// instance state on <see cref="ProductionService"/> itself, for the same reason every sibling
/// story's own lock registry is split out: <see cref="ProductionService"/> reads a per-request
/// Scoped <c>Tenancy.ICurrentTenantProvider</c>, and a Singleton cannot safely capture it.
///
/// Keyed one level more precisely than BIO-CRD-005/006's own (tenant, person, card class) locks:
/// this story's own invariants (job sequence numbers never reused, at most one open job) are
/// scoped to one SPECIFIC physical card issue, not to the whole (person, card class) family.
/// </summary>
public sealed class ProductionJobLockRegistry
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    public async Task<IDisposable> AcquireAsync(string tenantId, string personId, string cardClass, int sequenceNumber, CancellationToken cancellationToken)
    {
        var key = $"{tenantId}\u0000{personId}\u0000{cardClass}\u0000{sequenceNumber}";
        var gate = _locks.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        return new Releaser(gate);
    }

    private sealed class Releaser(SemaphoreSlim gate) : IDisposable
    {
        public void Dispose() => gate.Release();
    }
}

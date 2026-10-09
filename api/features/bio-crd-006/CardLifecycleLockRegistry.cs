using System.Collections.Concurrent;

namespace Biolog.Features.CardLifecycle;

/// <summary>
/// Per-(tenant, person, card class) in-process locking - own copy of the same split BIO-CRD-005's
/// own <c>CardIssueLockRegistry</c> established (a Singleton cannot safely capture the Scoped
/// <c>Tenancy.ICurrentTenantProvider</c> <see cref="CardLifecycleService"/> reads).
///
/// Keyed the same (tenant, person, card class) granularity as BIO-CRD-005's own lock: report,
/// recover and activate-replacement (whose sweep spans every earlier sequence number) must all
/// serialise against the same key, or a report racing an activate-replacement's sweep could
/// leave AC04's "every earlier card permanently non-reactivatable" invariant only partially
/// applied.
/// </summary>
public sealed class CardLifecycleLockRegistry
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    public async Task<IDisposable> AcquireAsync(string tenantId, string personId, string cardClass, CancellationToken cancellationToken)
    {
        var key = $"{tenantId}\u0000{personId}\u0000{cardClass}";
        var gate = _locks.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        return new Releaser(gate);
    }

    private sealed class Releaser(SemaphoreSlim gate) : IDisposable
    {
        public void Dispose() => gate.Release();
    }
}

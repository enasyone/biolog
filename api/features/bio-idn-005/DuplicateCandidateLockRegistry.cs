using System.Collections.Concurrent;

namespace Biolog.Features.DuplicateReview;

/// <summary>
/// Per-<c>(TenantId, PersonIdLow, PersonIdHigh)</c> in-process locking, held in its own singleton
/// class rather than as instance state on <see cref="DuplicateReviewService"/> - identical
/// reasoning to every sibling story's own lock registry: <see cref="DuplicateReviewService"/>
/// reads a per-request <see cref="Tenancy.ICurrentTenantProvider"/>, so it must be registered
/// Scoped, and a Singleton cannot safely capture a Scoped dependency.
///
/// <b>Lock scope reasoned explicitly, not assumed by analogy</b> (BIO-IDN-003-QA-01's lesson).
/// A candidate PAIR's own invariants - at most one record per pair, CAS on its decision - are
/// scoped to exactly that pair. Unlike BIO-IDN-003-QA-01's tenant-wide uniqueness invariant
/// (where a per-record lock was NOT enough, because the invariant spanned every person in the
/// tenant), nothing about raising, augmenting or deciding one candidate constrains any OTHER
/// pair - two callers scanning two entirely different person-pairs can proceed fully in
/// parallel with no shared invariant between them. A single per-pair lock is therefore the
/// correct and sufficient scope here.
/// </summary>
public sealed class DuplicateCandidateLockRegistry
{
    private readonly ConcurrentDictionary<(string TenantId, string PersonIdLow, string PersonIdHigh), SemaphoreSlim> _locks = new();

    public async Task<IDisposable> AcquireAsync(string tenantId, string personIdLow, string personIdHigh, CancellationToken cancellationToken)
    {
        var gate = _locks.GetOrAdd((tenantId, personIdLow, personIdHigh), static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        return new Releaser(gate);
    }

    private sealed class Releaser(SemaphoreSlim gate) : IDisposable
    {
        public void Dispose() => gate.Release();
    }
}

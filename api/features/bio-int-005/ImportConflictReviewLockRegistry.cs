using System.Collections.Concurrent;

namespace Biolog.Features.ImportConflictReview;

/// <summary>
/// Per-<c>(TenantId, MappingId, ExternalKey)</c> in-process locking, held in its own singleton
/// class rather than as instance state on <see cref="ImportConflictReviewService"/> - identical
/// reasoning to every sibling story's own lock registry: <see cref="ImportConflictReviewService"/>
/// reads a per-request <see cref="Tenancy.ICurrentTenantProvider"/>, so it must be registered
/// Scoped, and a Singleton cannot safely capture a Scoped dependency.
///
/// <b>Lock scope reasoned explicitly, not assumed by analogy</b> (BIO-IDN-003-QA-01's lesson).
/// One conflict review row's own invariants (at most one record per external key, CAS on its
/// decision) are scoped to exactly that row. Nothing about raising, augmenting, withdrawing or
/// deciding one row constrains any OTHER row - two callers acting on two entirely different
/// external keys can proceed fully in parallel with no shared invariant between them. A single
/// per-row lock is therefore the correct and sufficient scope here (same reasoning
/// <see cref="Domain.ImportConflictReview"/>'s sibling, BIO-IDN-005's <c>DuplicateCandidateLockRegistry</c>,
/// already applied to its own per-pair lock).
/// </summary>
public sealed class ImportConflictReviewLockRegistry
{
    private readonly ConcurrentDictionary<(string TenantId, string MappingId, string ExternalKey), SemaphoreSlim> _locks = new();

    public async Task<IDisposable> AcquireAsync(string tenantId, string mappingId, string externalKey, CancellationToken cancellationToken)
    {
        var gate = _locks.GetOrAdd((tenantId, mappingId, externalKey), static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        return new Releaser(gate);
    }

    private sealed class Releaser(SemaphoreSlim gate) : IDisposable
    {
        public void Dispose() => gate.Release();
    }
}

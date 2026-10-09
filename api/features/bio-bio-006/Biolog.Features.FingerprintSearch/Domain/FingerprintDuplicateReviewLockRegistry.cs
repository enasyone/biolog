using System.Collections.Concurrent;

namespace Biolog.Features.FingerprintSearch.Domain;

/// <summary>
/// BIO-BIO-006-QA-02 (Chioma acceptance, High). Per-<c>(TenantId, PersonIdLow, PersonIdHigh)</c>
/// in-process locking, held in its own singleton class - the identical shape and reasoning
/// BIO-IDN-005's own <c>DuplicateCandidateLockRegistry</c> documents for its structurally
/// identical aggregate.
///
/// <b>The race this closes.</b> <see cref="Service.FingerprintSearchService.RaiseOrAugmentReviewAsync"/>'s
/// read-check-write (read <c>GetAsync</c>, then either <c>CreateAsync</c> or
/// <c>ReplaceAsync</c>) was not atomic: two simultaneous searches that both first observe the
/// SAME (subject, candidate) pair as a new possible duplicate both read <c>null</c>, and both
/// then call <c>CreateAsync</c> - the repository's own uniqueness check correctly rejects the
/// second write, but nothing in the service caught that and converged, so one of the two
/// otherwise-legitimate concurrent callers received an unhandled
/// <see cref="InvalidOperationException"/> instead of the SAME successfully-raised entry every
/// other concurrent caller observing the same real-world event should converge on. Acquiring
/// this lock across the whole read-check-write sequence (mirrored for <see cref="Service.FingerprintSearchService.DecideAsync"/>'s
/// own read-check(version)-write too, for the identical reason) serializes only operations on
/// the SAME pair; two callers acting on two entirely different person-pairs still proceed fully
/// in parallel, since nothing about one pair's invariants constrains any other.
///
/// <b>Lock scope reasoned explicitly, not assumed by analogy</b> (BIO-IDN-003-QA-01's lesson,
/// reused via BIO-IDN-005's own header). A pair's own invariants (at most one entry per pair,
/// CAS on its decision) are scoped to exactly that pair - a single per-pair lock is the correct
/// and sufficient scope, the same reasoning BIO-IDN-005 already applied.
/// </summary>
public sealed class FingerprintDuplicateReviewLockRegistry
{
    private readonly ConcurrentDictionary<(string TenantId, string PersonIdLow, string PersonIdHigh), SemaphoreSlim> _locks = new();

    public async Task<IDisposable> AcquireAsync(string tenantId, string personIdLow, string personIdHigh, CancellationToken cancellationToken)
    {
        var gate = _locks.GetOrAdd((tenantId, personIdLow, personIdHigh), static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new Releaser(gate);
    }

    private sealed class Releaser(SemaphoreSlim gate) : IDisposable
    {
        public void Dispose() => gate.Release();
    }
}

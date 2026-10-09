namespace Biolog.Features.CardProduction.Domain;

/// <summary>
/// BIO-OPS-003-AC04 round 2 (SY-OPS003-02, Kemi's architectural ruling). This project's own
/// <see cref="ProductionJobLockRegistry"/> and BIO-CRD-006's own
/// <c>CardLifecycle.CardLifecycleLockRegistry</c> are fully independent locks - BIO-CRD-006's
/// <c>CardLifecycleService.ActivateReplacementAsync</c> reads this story's own latest production
/// status, then later activates the replacement, all under only ITS lock. Nothing previously
/// excluded a concurrent mutation here (create/print/encode/readback/reject/cancel/offline-
/// complete) from landing between that read and that later activation - a decoupled mirror of
/// the identical interface BIO-CRD-006's own Domain folder carries (neither feature project may
/// reference the other, or the host project directly - <c>scripts/check-module-project-references.sh</c>).
///
/// Kemi's ruling, stated as an invariant, not an enumerated call-site list: EVERY method in
/// <see cref="ProductionService"/> that acquires <see cref="ProductionJobLockRegistry"/> for a
/// given key must ALSO acquire this, nested the same way, for that same key - including any
/// future mutating method, not only the ones named when this was specified. The real
/// implementation is an adapter in <c>Biolog.Api.Modules.BioCrd007Module</c> wrapping the SAME
/// shared, host-layer <c>Biolog.Api.Cards.CardActivationBarrierLockRegistry</c> singleton
/// BIO-CRD-006's own module also wraps.
///
/// <b>Deadlock safety (checked before this type was specified).</b> This project never acquires
/// BIO-CRD-006's own lock, and BIO-CRD-006 never acquires this project's own
/// <see cref="ProductionJobLockRegistry"/> - the only cross-feature contention point is the one
/// shared resource this interface reaches, which cannot form a cycle on its own.
/// </summary>
public interface ICardActivationBarrier
{
    Task<IDisposable> AcquireAsync(string tenantId, string personId, string cardClass, int sequenceNumber, CancellationToken cancellationToken);
}

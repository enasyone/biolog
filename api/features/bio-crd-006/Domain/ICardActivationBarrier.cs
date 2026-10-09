namespace Biolog.Features.CardLifecycle.Domain;

/// <summary>
/// BIO-OPS-003-AC04 round 2 (SY-OPS003-02, Kemi's architectural ruling). Round 1's
/// <see cref="IProductionStatusGateway"/> closed the SEQUENTIAL gap (a replacement activating
/// without ever consulting production status) but not a genuine TIME-OF-CHECK-TO-TIME-OF-USE
/// race: <see cref="CardLifecycle.CardLifecycleService.ActivateReplacementAsync"/> reads
/// production status under only its own <c>CardLifecycleLockRegistry</c>, while BIO-CRD-007's
/// own <c>ProductionJobLockRegistry</c> is a fully independent lock - a concurrent reprint or
/// failure on BIO-CRD-007's side can land between that read and this method's later activation
/// call, activating against a stale production snapshot.
///
/// Kemi's ruling: shared per-card serialization, not a BIO-CRD-007-owned coordinator (that would
/// invert round 1's "complexity stays where it lives" reasoning). This is this project's own
/// narrow seam onto the real, host-layer <c>Biolog.Api.Cards.CardActivationBarrierLockRegistry</c>
/// - a cross-feature primitive neither this project nor BIO-CRD-007 can own outright (neither
/// feature project may reference the other, or the host project directly -
/// <c>scripts/check-module-project-references.sh</c>), so each feature gets its own decoupled
/// interface, same shape as <see cref="IProductionStatusGateway"/>'s own split. The real
/// implementation is an adapter in <c>Biolog.Api.Modules.BioCrd006Module</c> wrapping that SAME
/// singleton instance BIO-CRD-007's own module also wraps - one shared lock, two narrow seams.
///
/// <see cref="CardLifecycle.CardLifecycleService.ActivateReplacementAsync"/> acquires this,
/// nested inside its own <c>CardLifecycleLockRegistry</c> acquisition, keyed by the REPLACEMENT's
/// own sequence number, held from right before the production-status check through the real
/// activation call completing - see that method's own header for why this fully closes the race
/// without needing to span every earlier sequence number the revocation sweep also touches (that
/// sweep has no production check of its own to protect).
/// </summary>
public interface ICardActivationBarrier
{
    Task<IDisposable> AcquireAsync(string tenantId, string personId, string cardClass, int sequenceNumber, CancellationToken cancellationToken);
}

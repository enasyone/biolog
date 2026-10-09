using System.Collections.Concurrent;

namespace Biolog.Api.Cards;

/// <summary>
/// <b>Kemi's architectural ruling, BIO-OPS-003 round 2 (2026-10-07).</b> Seyi's round-2 review
/// (`SY-OPS003-02`, High) found a genuine cross-feature TOCTOU race, a different mechanism from
/// round 1's sequential composition gap (closed in `bbcd6e1`): `Biolog.Features.CardLifecycle`'s
/// own <c>CardLifecycleLockRegistry</c> and `Biolog.Features.CardProduction`'s own
/// <c>ProductionJobLockRegistry</c> are two fully independent
/// <c>ConcurrentDictionary&lt;string, SemaphoreSlim&gt;</c> singletons, verified directly, zero
/// shared state. `CardLifecycleService.ActivateReplacementAsync` reads the latest production job
/// status, then later sweeps/revokes earlier cards and activates the replacement - all under
/// only its own lock. Every one of `ProductionService`'s mutating entry points (create, the
/// shared print/encode/readback step recorder, reject, cancel, complete-offline) acquires only
/// its own lock at the identical `(tenant, person, cardClass, sequenceNumber)` key. Nothing
/// excludes one side from the other: a concurrent reprint/failure can land between the
/// production-status read and the later activation call, activating a replacement against a
/// stale production snapshot.
///
/// <b>Why this lives here, not in either feature project (the ruling's own required
/// disclosure).</b> This is a mechanism BOTH `bio-crd-006` and `bio-crd-007` need, with neither
/// one the natural sole owner of it - the same placement shape already established for
/// <see cref="Tenancy.DemoSessionTenantResolver"/> and
/// <see cref="Features.DeviceRegistry.ICrossTenantDeviceLookup"/> (a cross-feature primitive
/// placed at the host layer, each feature's own `Biolog.Api.Modules.*Module` wiring an adapter
/// onto the SAME singleton instance, rather than either feature project reaching into the
/// other's isolated project - forbidden regardless of which "side" would host it, per
/// `scripts/check-module-project-references.sh`'s own one-way-dependency rule). Ruled the same
/// tier as the device-lookup placement decision: a concurrency-primitive location choice, not a
/// frozen-schema or tenant-resolution-channel change, so it does NOT trigger BIO-ARC-006-AC04's
/// change-control process and needs no new ADR.
///
/// <b>Keying - deliberately CRD-007's own existing granularity, which nests cleanly inside
/// CRD-006's broader key.</b> `(tenantId, personId, cardClass, sequenceNumber)` - CRD-006's own
/// `CardLifecycleLockRegistry` key `(tenantId, personId, cardClass)` is a strict prefix of this
/// one, so CRD-006's replacement-activation sweep (which spans every earlier sequence number)
/// correctly acquires THIS lock once per sequence number it touches, never needing a coarser key
/// of its own.
///
/// <b>Required usage - stated as an invariant, not an enumerated call-site list, so a future
/// 7th mutating method never silently falls outside the fix:</b> every method that acquires
/// <c>ProductionJobLockRegistry</c> for a given key must ALSO acquire this registry for the same
/// key, and `CardLifecycleService.ActivateReplacementAsync` must acquire this registry (nested
/// inside its own `CardLifecycleLockRegistry` acquisition) spanning from the production-status
/// read through the activation call, for every sequence number its sweep touches.
///
/// <b>Deadlock safety, checked before this type was specified.</b> `bio-crd-006` never acquires
/// `ProductionJobLockRegistry` and `bio-crd-007` never acquires `CardLifecycleLockRegistry` -
/// the only cross-feature contention point is THIS shared lock, a single resource per key. One
/// shared resource cannot form a cycle; a cycle would require two-plus shared resources acquired
/// in opposing orders, which this design never introduces.
///
/// <b>Standing obligation for any FUTURE cross-feature lock need, the same shape
/// <see cref="Features.DeviceRegistry.ICrossTenantDeviceLookup"/>'s own header already
/// establishes for a cross-tenant lookup</b>: reuse THIS type (a new key shape is still this
/// type, not a reason to invent a parallel one) rather than adding a third registry - any
/// genuinely different keying need still needs Kemi's approval first, the same governance tier
/// this ruling itself used.
/// </summary>
public sealed class CardActivationBarrierLockRegistry
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

namespace Biolog.Api.Features.DeviceRegistry;

/// <summary>
/// The single, deliberate exception to every other method in this feature requiring a
/// resolved <see cref="Tenancy.TenantContext"/> (BIO-DEV-001-AC04) - its own, narrower
/// interface rather than an overload on <see cref="IDeviceRepository"/>, so the exception is
/// visible at the type level, not buried as one option among several identically-named
/// methods.
///
/// <b>Kemi's architectural ruling on BIO-MOB-003's acceptance FAIL (2026-09-23).</b>
/// BIO-MOB-003's central sync intake has no ambient tenant channel to read - resolving the
/// tenant FROM the authenticated device is the entire point of that story's design (its own
/// <c>IDeviceTenantResolutionGateway</c> doc explains why the request body's own
/// <c>tenant_id</c> may never be trusted as the routing source, per ADR-0002 Decision 1.1).
/// That created a genuine circular dependency: the only existing device lookup
/// (<see cref="IDeviceRepository.GetAsync"/>) itself requires an already-resolved tenant. This
/// interface is the fix - a narrow, cross-tenant read that answers "what tenant does THIS
/// device, named by its own id, belong to," never "which devices exist across tenants."
///
/// <b>internal, not public - the safety property is structural, not a documentation
/// convention.</b> Only code inside this assembly can reach it - in practice only
/// <c>Biolog.Api.Modules.BioMob003Module</c>'s adapter, which resolves it directly (not
/// through <c>DeviceService</c>: the built-in DI container activates <c>DeviceService</c> by
/// reflection, which requires a public constructor, so this internal capability is registered
/// and consumed on its own via a manual factory lambda instead). That adapter may call this
/// interface ONLY after independently authenticating the calling device via a
/// secret-possession proof (mirroring BIO-PHO-002's
/// <c>IDeviceAuthenticator</c>/<c>IDeviceCredentialStore</c>). This
/// realises ADR-0002 Decision 1.1's <c>principal</c> channel for a device-secret-authenticated
/// claim - the same category as that channel's own "device certificate" example, via a
/// shared-secret attestation rather than PKI/mTLS. It does not weaken ADR-0002 Decision 4.1's
/// "the binding is looked up, never supplied": the database binding is still resolved from
/// the tenant id this seam returns, never from anything a caller supplies directly.
///
/// This does not reopen ADR-0002 Decision 4.3's cross-tenant-not-found protection for any
/// OTHER consumer: it is not registered against <see cref="IDeviceRepository"/> and grants no
/// caller the ability to probe an arbitrary device id without first proving possession of
/// that exact device's own credential - the same "cannot distinguish unregistered from wrong
/// secret" discipline <c>IDeviceCredentialStore.Validate</c> already applies.
/// </summary>
internal interface ICrossTenantDeviceLookup
{
    /// <summary>Returns the full <see cref="Device"/> record (not just its <see cref="Device.TenantId"/>) so a caller can also apply BIO-DEV-001's own <see cref="DeviceStatus.Active"/> gate - null if no device with this id exists, in any tenant.</summary>
    Task<Device?> GetByIdAsync(string deviceId, CancellationToken cancellationToken);
}

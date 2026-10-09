namespace Biolog.Features.OfflineSync.Domain;

/// <summary>A device authenticated and resolved by <see cref="IDeviceTenantResolutionGateway"/> - the tenant it may write on behalf of.</summary>
public sealed record ResolvedDevice(string DeviceId, string TenantId);

/// <summary>
/// This feature's own narrow seam for "resolve the tenant this authenticated device belongs
/// to" - the architectural core of Kemi's ruling on BIO-MOB-003's acceptance FAIL.
///
/// <b>Why this exists instead of trusting the request body.</b>
/// <c>offline-mutation.schema.json</c>'s <c>tenant_id</c> is a request BODY field. ADR-0002
/// Decision 1.1 is explicit that a body field is not a channel, and trusting one to route a
/// mutation is the cross-tenant-substitution threat as a shipped feature, not a hypothetical -
/// any caller who can construct a JSON body could claim to write into any tenant. This gateway
/// is the alternative: the tenant a mutation is applied to comes ONLY from the device's own,
/// independently registered <c>TenantId</c> (<c>api/Biolog.Api/Features/DeviceRegistry/Device.cs</c>)
/// - not from anything the caller supplies in this request. The payload's own <c>tenant_id</c>
/// is still read, but only ever as a claim to CROSS-VALIDATE against this independently
/// resolved value (<see cref="Domain.OfflineSyncError.TenantMismatch"/>) - the same
/// claim-vs-resolution discipline ADR-0002/0006 already apply elsewhere in this codebase, here
/// protecting real data routing rather than only audit attribution.
///
/// Deliberately NOT a <c>ProjectReference</c> to <c>api/Biolog.Api/Features/DeviceRegistry</c>,
/// even though it is a public, in-host type this feature COULD reference directly - the real
/// implementation is an adapter in <c>Biolog.Api.Modules.BioMob003Module</c> wrapping BIO-DEV-001's
/// own already-registered <c>DeviceService.GetAsync</c>, requiring <c>DeviceStatus.Active</c>,
/// mirroring every sibling feature's own device-existence gateway.
/// </summary>
public interface IDeviceTenantResolutionGateway
{
    /// <summary>Null if the device does not exist or is not <c>DeviceStatus.Active</c> - a retired or not-yet-activated device must never resolve a tenant to write into.</summary>
    Task<ResolvedDevice?> ResolveAsync(string deviceId, CancellationToken cancellationToken = default);
}

namespace Biolog.Features.AccessCache.Domain;

/// <summary>A device authenticated and resolved by <see cref="IDeviceTenantResolutionGateway"/> - the tenant it may read a revocation delta for, or submit a presentation event on behalf of.</summary>
public sealed record ResolvedDevice(string DeviceId, string TenantId);

/// <summary>
/// This feature's own narrow seam for "resolve the tenant this authenticated device belongs
/// to" - mirrors BIO-MOB-003's <c>IDeviceTenantResolutionGateway</c> exactly, the same
/// architectural shape Kemi's ruling established (2026-09-23) and god extended to this story
/// (2026-09-24). A device pulling its tenant's revocation deny-list, or submitting a
/// presentation-event log, has no host/principal channel (ADR-0002 Decision 1) - the tenant it
/// may act on comes only from its own independently registered record, never a caller-supplied
/// value.
///
/// Deliberately NOT a <c>ProjectReference</c> to <c>api/Biolog.Api/Features/DeviceRegistry</c> -
/// the real implementation is an adapter in <c>Biolog.Api.Modules.BioAcc003Module</c> wrapping
/// the same <c>ICrossTenantDeviceLookup</c> escape hatch BIO-MOB-003's own adapter already
/// consumes (one shared, already-reviewed seam on DeviceRegistry's side, two independent
/// feature-side gateways consuming it).
/// </summary>
public interface IDeviceTenantResolutionGateway
{
    /// <summary>Null if the device does not exist or is not active - a retired or not-yet-activated device must never resolve a tenant.</summary>
    Task<ResolvedDevice?> ResolveAsync(string deviceId, CancellationToken cancellationToken = default);
}

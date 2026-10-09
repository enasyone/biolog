namespace Biolog.Features.OfflineEnrolment.Domain;

/// <summary>A device authenticated and resolved by <see cref="IDeviceTenantResolutionGateway"/> - the tenant it may submit an enrolment package for, or read its capture policy for.</summary>
public sealed record ResolvedDevice(string DeviceId, string TenantId);

/// <summary>
/// This feature's own narrow seam for "resolve the tenant this authenticated device belongs
/// to" - mirrors BIO-MOB-003's/BIO-ACC-003's <c>IDeviceTenantResolutionGateway</c> exactly, the
/// same architectural shape Kemi's ruling established (2026-09-23) for BIO-MOB-003 and god
/// extended to BIO-ACC-003 (2026-09-24). A device submitting an enrolment package, or pulling
/// its tenant's offline capture policy, has no host/principal channel (ADR-0002 Decision 1) -
/// the tenant it may act on comes only from its own independently registered record, never a
/// caller-supplied value.
///
/// Deliberately NOT a <c>ProjectReference</c> to <c>api/Biolog.Api/Features/DeviceRegistry</c> -
/// the real implementation is an adapter in <c>Biolog.Api.Modules.BioMob005Module</c> wrapping
/// the same <c>ICrossTenantDeviceLookup</c> escape hatch BIO-MOB-003's and BIO-ACC-003's own
/// adapters already consume (one shared, already-reviewed seam on DeviceRegistry's side, three
/// independent feature-side gateways consuming it).
/// </summary>
public interface IDeviceTenantResolutionGateway
{
    /// <summary>Null if the device does not exist or is not active - a retired or not-yet-activated device must never resolve a tenant.</summary>
    Task<ResolvedDevice?> ResolveAsync(string deviceId, CancellationToken cancellationToken = default);
}

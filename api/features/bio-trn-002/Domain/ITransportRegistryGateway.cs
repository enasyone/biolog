namespace Biolog.Features.TransportEvents.Domain;

/// <summary>Route existence/active-status plus which stops it currently lists - everything <see cref="TransportEventService.CaptureAsync"/> needs from a route in one round trip.</summary>
public sealed record TransportRouteInfo(string Id, bool IsActive, IReadOnlyList<string> StopIds);

/// <summary>AC05's demo bridge seed - a real, active route/stop/vehicle triple in the fixed demo tenant, found-or-created idempotently (see <see cref="ITransportRegistryGateway.EnsureDemoSeedAsync"/>).</summary>
public sealed record TransportDemoSeed(string RouteId, string StopId, string VehicleId);

/// <summary>
/// This feature's own narrow, read-only seam onto BIO-TRN-001's Route/Vehicle/Stop registries -
/// the same gateway-not-<c>ProjectReference</c> shape as <see cref="IPersonExistenceGateway"/>.
/// AC01's route/vehicle/stop references must be real, and a route must be
/// <c>RouteStatus.Active</c> to capture a board/alight against (boarding on a Draft/Retired route
/// is a validation error, the same rule BIO-TRN-001's own <c>PersonAssignmentService.CreateAsync</c>
/// applies) - a vehicle must be <c>VehicleStatus.Active</c> (in-service; unlike BIO-TRN-001's own
/// device-linkage gateway, this IS a status check, not existence-only: capturing a real board
/// event against an out-of-service or retired vehicle would record a physically impossible fact).
/// A <see cref="TransportEvent.StopId"/>, when present, must be one of the target route's own
/// stops AND itself <c>StopStatus.Active</c> - the identical cross-aggregate consistency rule
/// BIO-TRN-001's own <c>PersonAssignment.StopId</c> already enforces, re-applied here rather than
/// re-derived from scratch. <c>Biolog.Api</c>'s <c>BioTrn002Module</c> implements this by wrapping
/// BIO-TRN-001's own already-registered (internal, <c>InternalsVisibleTo Biolog.Api</c>)
/// <c>IRouteRepository</c>/<c>IVehicleRepository</c>/<c>IStopRepository</c> directly.
/// </summary>
public interface ITransportRegistryGateway
{
    Task<TransportRouteInfo?> GetRouteAsync(string routeId, CancellationToken cancellationToken = default);

    Task<bool> VehicleIsActiveAsync(string vehicleId, CancellationToken cancellationToken = default);

    Task<bool> StopIsActiveAsync(string stopId, CancellationToken cancellationToken = default);
}

/// <summary>
/// AC05's demo bridge (<c>Demo.TransportEventDemoBridgeService</c>, registered Singleton) needs a
/// real, active route/stop/vehicle to capture demo events against, but does not know whether
/// BIO-TRN-001's OWN demo bridge (a completely separate Singleton, possibly never yet called in
/// this process's lifetime - a user could click "View boarding/alighting log" before ever
/// clicking "Assign route") has already seeded one. Finds the shared demo route by its fixed name
/// if it already exists (either bridge may have created it first) or creates it - genuine domain
/// construction through BIO-TRN-001's own aggregate factories, not a second, divergent seed.
/// Idempotent and safe to call from either story's bridge, in either order.
///
/// <b>Deliberately a SEPARATE interface from <see cref="ITransportRegistryGateway"/>, not one
/// more method on it.</b> <see cref="ITransportRegistryGateway"/>'s own adapter resolves the
/// AMBIENT tenant via <c>ITenantContextAccessor</c> (Scoped) for the real, tenant-scoped capture
/// path - registering that adapter Singleton so the ALSO-Singleton demo bridge could hold it
/// would be exactly the captive-dependency bug (Singleton capturing a Scoped dependency) this
/// codebase's own BIO-IDN-001 lesson already closed once. This provisioner takes
/// <paramref name="tenantId"/> explicitly (the demo bridge's own fixed constant, never resolved
/// ambiently) and its adapter depends only on the Singleton repositories - safe to register and
/// consume as Singleton with no captive dependency at all.
/// </summary>
public interface ITransportDemoSeedProvisioner
{
    Task<TransportDemoSeed> EnsureDemoSeedAsync(string tenantId, CancellationToken cancellationToken = default);
}

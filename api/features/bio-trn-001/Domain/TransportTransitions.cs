namespace Biolog.Features.Transport.Domain;

/// <summary>
/// The legality tables each service checks before calling a mutation method, and each in-memory
/// repository independently re-checks at the persistence boundary - the same dual-boundary
/// discipline BIO-IDN-001's <c>PersonLifecycleTransitions</c>/<c>InMemoryPersonRepository</c> pair
/// established, reapplied by BIO-VIS-001's own <c>VisitorTransitions</c>.
/// </summary>
public static class TransportTransitions
{
    /// <summary>AC01: Route/Stop are one-way - Draft -&gt; Active -&gt; Retired (Stop has no Draft state, just Active -&gt; Retired). Neither ever moves backwards.</summary>
    public static bool IsAllowedRouteTransition(RouteStatus from, RouteStatus to) =>
        (from, to) switch
        {
            (RouteStatus.Draft, RouteStatus.Active) => true,
            (RouteStatus.Active, RouteStatus.Retired) => true,
            (RouteStatus.Draft, RouteStatus.Retired) => true,
            _ => false,
        };

    public static bool IsAllowedStopTransition(StopStatus from, StopStatus to) =>
        from == StopStatus.Active && to == StopStatus.Retired;

    /// <summary>AC01: Vehicle is NOT strictly one-way - Active and OutOfService cycle (a vehicle taken out of service for repairs legitimately returns to Active), but Retired is terminal from either.</summary>
    public static bool IsAllowedVehicleTransition(VehicleStatus from, VehicleStatus to) =>
        (from, to) switch
        {
            (VehicleStatus.Active, VehicleStatus.OutOfService) => true,
            (VehicleStatus.OutOfService, VehicleStatus.Active) => true,
            (VehicleStatus.Active, VehicleStatus.Retired) => true,
            (VehicleStatus.OutOfService, VehicleStatus.Retired) => true,
            _ => false,
        };
}

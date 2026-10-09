namespace Biolog.Features.Transport.Domain;

/// <summary>
/// AC01 - a route's own lifecycle. One-way narrowing, exactly like PERSON's own lifecycle
/// (BIO-IDN-001) and <see cref="VehicleStatus"/>/<see cref="StopStatus"/> below: no un-retiring,
/// no returning a Retired route to Draft. A route only carries person assignments while
/// <see cref="Active"/> (see <c>PersonAssignmentService.CreateAsync</c>).
/// </summary>
public enum RouteStatus
{
    Draft,
    Active,
    Retired,
}

public static class RouteStatusExtensions
{
    public static string ToWireString(this RouteStatus status) => status switch
    {
        RouteStatus.Draft => "draft",
        RouteStatus.Active => "active",
        RouteStatus.Retired => "retired",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "unreachable: exhaustive switch"),
    };
}

/// <summary>AC01 - a vehicle's own lifecycle, independent of any route it may be assigned to elsewhere (this story does not model route-to-vehicle assignment, only route/vehicle/stop/person as four independently versioned registries - see docs/features/bio-trn-001/README.md "Deliberately out of scope").</summary>
public enum VehicleStatus
{
    Active,
    OutOfService,
    Retired,
}

public static class VehicleStatusExtensions
{
    public static string ToWireString(this VehicleStatus status) => status switch
    {
        VehicleStatus.Active => "active",
        VehicleStatus.OutOfService => "out_of_service",
        VehicleStatus.Retired => "retired",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "unreachable: exhaustive switch"),
    };
}

/// <summary>AC01 - a stop's own lifecycle. A route's own <see cref="Route.Stops"/> list may still reference a since-retired stop (history is not silently rewritten); <see cref="PersonAssignmentService"/> is the one place that requires the CURRENT stop status to be <see cref="Active"/> before it may be assigned to.</summary>
public enum StopStatus
{
    Active,
    Retired,
}

public static class StopStatusExtensions
{
    public static string ToWireString(this StopStatus status) => status switch
    {
        StopStatus.Active => "active",
        StopStatus.Retired => "retired",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "unreachable: exhaustive switch"),
    };
}

/// <summary>
/// AC02 - a derived (never persisted) read-time overlay of a <see cref="PersonAssignment"/>'s own
/// validity window: the same discipline BIO-IDN-002 established for time-based status ("refuse
/// auto-flip ... compute in effect at read time") and BIO-ATT-001/BIO-VIS-001 already applied for
/// their own derived status. A genuinely separate type from anything persisted on
/// <see cref="PersonAssignment"/> - nothing here is ever written back to the stored record.
/// </summary>
public enum EffectiveAssignmentStatus
{
    Upcoming,
    Active,
    Ended,
}

public static class EffectiveAssignmentStatusExtensions
{
    public static string ToWireString(this EffectiveAssignmentStatus status) => status switch
    {
        EffectiveAssignmentStatus.Upcoming => "upcoming",
        EffectiveAssignmentStatus.Active => "active",
        EffectiveAssignmentStatus.Ended => "ended",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "unreachable: exhaustive switch"),
    };
}

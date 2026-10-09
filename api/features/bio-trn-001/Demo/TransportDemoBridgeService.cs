using Biolog.Features.Transport.Audit;
using Biolog.Features.Transport.Domain;
using Biolog.Features.Transport.Identifiers;

namespace Biolog.Features.Transport.Demo;

/// <summary>
/// AC05's own bridge: <c>web/features/transport/live.ts</c>'s <c>transportAssignRouteLivePort</c>
/// POSTs a bare, headerless request to <c>/api/v1/transport/assign-route</c> - BIO-UX-005's own
/// scaffolding, by design (no login/session story exists yet, the same disclosed reasoning
/// BIO-ATT-001's/BIO-VIS-001's own demo bridges already established for an identically shaped
/// headerless UI caller). This bridge operates against one FIXED, already-public SYNTHETIC demo
/// tenant, genuinely exercising this feature's own domain code (Route/Stop/Vehicle registration,
/// real <see cref="PersonAssignment"/> creation) - a real, connected live implementation of the
/// mapped action, not a mock.
///
/// <b><c>transportLivePort</c> ("View boarding/alighting log") moved to BIO-TRN-002.</b> This
/// class used to also answer that route with a registry-summary placeholder, disclosed at the
/// time as standing in for a boarding/alighting event log that did not exist yet in THIS story's
/// scope (AC01-04 are the Route/Vehicle/Stop/PersonAssignment structural model only). BIO-TRN-002
/// now provides that capability and maps/answers the route itself
/// (<c>Biolog.Features.TransportEvents.Demo.TransportEventDemoBridgeService</c>, reusing this
/// same demo tenant/route/stop/vehicle by name via its own
/// <c>ITransportRegistryGateway.EnsureDemoSeedAsync</c>, found-or-created, not duplicated) - see
/// docs/features/bio-trn-002/README.md "Why this story edits BIO-TRN-001's own endpoint mapping".
///
/// <b>Why a dedicated bridge instead of routing through the tenant-scoped services.</b> Those
/// services require a real resolved <see cref="Tenancy.ICurrentTenantProvider.TenantId"/> (Scoped)
/// and, for assignment creation, a real permission/provenance-verified actor - a headerless demo
/// POST resolves neither. This bridge is registered Singleton and calls the SAME aggregate factory
/// methods and repositories the tenant-scoped services call (bypassing only the permission/
/// provenance CHECK, not the domain logic itself - the bridge's own fixed
/// <see cref="DemoUiActorId"/> service-account identity stands in for "the UI itself decided to
/// assign", the same convenience-path pattern BIO-VIS-001's own bridge uses for its
/// auto-approval step), against the fixed demo identifiers below.
///
/// The demo tenant id is the literal example value <c>contracts/schemas/identifier-registry.json</c>
/// itself publishes for the <c>tenant</c> type - the same synthetic value BIO-ATT-001's and
/// BIO-VIS-001's own bridges already use. Reused deliberately (not re-minted): this feature's own
/// repositories are completely separate stores from either sibling's, so there is no collision
/// risk. Demo person ids likewise reuse BIO-ATT-001's/BIO-VIS-001's own three synthetic demo
/// person ids. The seeded demo route is named to match <c>TRANSPORT_ROUTE_NAMES[0]</c>
/// ("Route A — Riverside Loop") so the wiring is visibly coherent with the synthetic dataset, not
/// a coincidental string.
/// </summary>
internal sealed class TransportDemoBridgeService(
    IRouteRepository routeRepository,
    IStopRepository stopRepository,
    IVehicleRepository vehicleRepository,
    IPersonAssignmentRepository assignmentRepository,
    ITransportAuditSink auditSink)
{
    public const string DemoTenantId = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77";
    public const string DemoUiActorId = "svc_transport_ui_bridge";
    private const string DemoRouteName = "Route A — Riverside Loop";

    private static readonly string[] DemoPersonIds =
    [
        "per_0199e0c1-9d40-7e52-b103-7c9a4e2f6b1d",
        "per_0199e0c2-1111-7222-8333-444455556666",
        "per_0199e0c2-2222-7333-9444-555566667777",
    ];

    private readonly SemaphoreSlim _gate = new(1, 1);
    private RouteRecord? _demoRoute;
    private StopRecord? _demoStop;
    private VehicleRecord? _demoVehicle;
    private int _assignRouteCallCount;

    /// <summary>"Assign route" (route-manifest.ts's secondary action). Deterministically cycles three demo people: assigns whichever slot does not already have a live assignment, through this story's own real domain code.</summary>
    public async Task<string> AssignNextDemoPersonAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var (route, stop, _) = await EnsureSeededAsync(cancellationToken);

            var slot = (int)((uint)Interlocked.Increment(ref _assignRouteCallCount) - 1) % DemoPersonIds.Length;
            var personId = DemoPersonIds[slot];

            var existing = await assignmentRepository.ListByPersonAsync(DemoTenantId, personId, cancellationToken);
            var now = UtcInstant.Now();
            if (existing.Any(a => TransportStatusEvaluator.EffectiveStatus(a, now) != EffectiveAssignmentStatus.Ended))
            {
                return $"Transport: this demo person is already assigned to {route.Name} (live).";
            }

            var assignment = PersonAssignment.Create(DemoTenantId, personId, route.Id, stop.Id, now, null, DemoUiActorId, now);
            await WriteAuditAsync("transport.assignment_created", "person_assignment", assignment.Id, cancellationToken);
            assignment = await assignmentRepository.CreateAsync(assignment, cancellationToken);

            return $"Transport: demo person assigned to {route.Name} via {stop.Name} (live).";
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<(RouteRecord Route, StopRecord Stop, VehicleRecord Vehicle)> EnsureSeededAsync(CancellationToken cancellationToken)
    {
        if (_demoRoute is not null && _demoStop is not null && _demoVehicle is not null)
        {
            return (_demoRoute, _demoStop, _demoVehicle);
        }

        var now = UtcInstant.Now();

        var stop = StopRecord.Create(DemoTenantId, campusId: null, "Riverside Gate", "Main pedestrian gate on the riverside loop", DemoUiActorId, now);
        await WriteAuditAsync("transport.stop_created", "stop", stop.Id, cancellationToken);
        stop = await stopRepository.CreateAsync(stop, cancellationToken);

        var route = RouteRecord.Create(
            DemoTenantId, campusId: null, DemoRouteName,
            [new RouteStopEntry { StopId = stop.Id, SequenceOrder = 1, EstimatedOffsetMinutes = 0 }],
            DemoUiActorId, now);
        await WriteAuditAsync("transport.route_created", "route", route.Id, cancellationToken);
        route = await routeRepository.CreateAsync(route, cancellationToken);

        var activated = route.Activate(DemoUiActorId, UtcInstant.Now());
        await WriteAuditAsync("transport.route_activated", "route", route.Id, cancellationToken);
        route = await routeRepository.ReplaceAsync(route, activated, cancellationToken);

        var vehicle = VehicleRecord.Create(DemoTenantId, campusId: null, "Bus 01", capacity: 40, deviceId: null, DemoUiActorId, now);
        await WriteAuditAsync("transport.vehicle_created", "vehicle", vehicle.Id, cancellationToken);
        vehicle = await vehicleRepository.CreateAsync(vehicle, cancellationToken);

        _demoStop = stop;
        _demoRoute = route;
        _demoVehicle = vehicle;
        return (route, stop, vehicle);
    }

    private async Task WriteAuditAsync(string action, string targetType, string targetId, CancellationToken cancellationToken)
    {
        var auditEvent = TransportAuditEventBuilder.Build(
            DemoTenantId,
            new AuditActor(ActorType.ServiceAccount, DemoUiActorId),
            action,
            new AuditTarget(targetType, targetId),
            AuditOutcome.Allowed,
            $"{action.Replace('.', '_')}_via_ui_bridge",
            CanonicalId.NewEventId(),
            null,
            new Dictionary<string, (object?, string)>());

        await auditSink.WriteAsync(auditEvent, cancellationToken);
    }
}

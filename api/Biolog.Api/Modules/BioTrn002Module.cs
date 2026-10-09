using Biolog.Api.Tenancy;
using Biolog.Features.TransportEvents;
using Biolog.Features.TransportEvents.Api;
using Biolog.Features.TransportEvents.Audit;
using Biolog.Features.TransportEvents.Demo;
using Biolog.Features.TransportEvents.Domain;
using Biolog.Features.TransportEvents.Tenancy;

namespace Biolog.Api.Modules;

/// <summary>
/// Boarding/alighting and bus-presence events (BIO-TRN-002), registered the same way every
/// sibling identity-wave module wraps its feature project.
///
/// <see cref="CurrentTenantProviderAdapter"/> only reads the already-shipped
/// <see cref="ITenantContextAccessor"/> (identical shape to every sibling module's own adapter).
///
/// <see cref="PersonExistenceGatewayAdapter"/> wraps BIO-IDN-001's own already-registered
/// <c>PersonService.GetAsync</c> directly. <see cref="TransportRegistryGatewayAdapter"/> wraps
/// BIO-TRN-001's own already-registered (internal, <c>InternalsVisibleTo Biolog.Api</c>)
/// <c>IRouteRepository</c>/<c>IVehicleRepository</c>/<c>IStopRepository</c> directly - reachable
/// here only because that project's own csproj grants this assembly internals visibility, the
/// same seam <c>BioCrd006Module</c>'s own cross-story adapter already established for its
/// dependency on BIO-CRD-005.
///
/// <see cref="TransportEventDemoBridgeService"/> is registered Singleton alongside the Singleton
/// in-memory repositories/audit sink it depends on directly (AC05's own headerless UI bridge -
/// takes over <c>/api/v1/transport/primary-action</c> from BIO-TRN-001's own bridge, see that
/// story's own <c>TransportDemoBridgeService.cs</c> header for the disclosed handoff).
/// <see cref="TransportDemoSeedProvisioner.EnsureDemoSeedAsync"/> is this module's own genuine
/// cross-story WRITE into BIO-TRN-001's registries (not merely a read) - a real, disclosed
/// exception to "gateways are read-only," justified because the demo bridge has no other way to
/// reach a route/stop/vehicle that either story's own bridge may be the first to need. Registered
/// as a SEPARATE Singleton adapter from the Scoped, ambient-tenant <see cref="TransportRegistryGatewayAdapter"/>
/// - a captive-dependency bug (Singleton capturing Scoped <see cref="ITenantContextAccessor"/>)
/// was caught and fixed during this story's own build, not shipped (see
/// docs/features/bio-trn-002/README.md "Why this story edits BIO-TRN-001's own endpoint mapping"
/// for the full record).
///
/// Depends on <see cref="TenantModule"/>, <see cref="BioIdn001Module"/> (registered as
/// <c>"PersonRegistry"</c>) and <see cref="BioTrn001Module"/> (registered as <c>"Transport"</c>).
/// </summary>
public sealed class BioTrn002Module : IBiologModule
{
    public string Name => "TransportEvents";
    public IReadOnlyCollection<string> DependsOn => ["Tenant", "PersonRegistry", "Transport"];

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ICurrentTenantProvider>(sp => new CurrentTenantProviderAdapter(sp.GetRequiredService<ITenantContextAccessor>()));

        services.AddSingleton<ITransportEventRepository, InMemoryTransportEventRepository>();
        services.AddSingleton<ITransportNotificationConfigRepository, InMemoryTransportNotificationConfigRepository>();
        services.AddSingleton<ITriggeredNotificationLog, InMemoryTriggeredNotificationLog>();
        services.AddSingleton<ITransportEventAuditSink, InMemoryTransportEventAuditSink>();
        services.AddSingleton<TransportEventLockRegistry>();
        services.AddSingleton<TransportNotificationConfigLockRegistry>();

        services.AddScoped<IPersonExistenceGateway>(
            sp => new PersonExistenceGatewayAdapter(sp.GetRequiredService<Biolog.Features.PersonRegistry.PersonService>()));
        services.AddScoped<ITransportRegistryGateway>(
            sp => new TransportRegistryGatewayAdapter(
                sp.GetRequiredService<Biolog.Features.Transport.Domain.IRouteRepository>(),
                sp.GetRequiredService<Biolog.Features.Transport.Domain.IVehicleRepository>(),
                sp.GetRequiredService<Biolog.Features.Transport.Domain.IStopRepository>(),
                sp.GetRequiredService<ITenantContextAccessor>()));
        services.AddSingleton<ITransportDemoSeedProvisioner>(
            sp => new TransportDemoSeedProvisioner(
                sp.GetRequiredService<Biolog.Features.Transport.Domain.IRouteRepository>(),
                sp.GetRequiredService<Biolog.Features.Transport.Domain.IVehicleRepository>(),
                sp.GetRequiredService<Biolog.Features.Transport.Domain.IStopRepository>()));

        services.AddScoped<TransportEventService>();
        services.AddScoped<TransportNotificationConfigService>();

        services.AddSingleton<TransportEventDemoBridgeService>();
    }

    public void Configure(IApplicationBuilder app)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        TransportEventEndpoints.Map(endpoints);
    }

    private sealed class CurrentTenantProviderAdapter(ITenantContextAccessor accessor) : ICurrentTenantProvider
    {
        public string? TenantId => accessor.Context?.TenantId;
    }

    private sealed class PersonExistenceGatewayAdapter(Biolog.Features.PersonRegistry.PersonService personService) : IPersonExistenceGateway
    {
        public async Task<bool> ExistsAsync(string personId, CancellationToken cancellationToken = default) =>
            await personService.GetAsync(personId, cancellationToken) is not null;
    }

    /// <summary>
    /// AC01's cross-story read seam onto BIO-TRN-001. Registered Scoped (see this class's own
    /// interface header for why): resolves the ambient tenant itself (the same
    /// <see cref="ITenantContextAccessor"/> every gateway adapter in this codebase reads).
    /// </summary>
    private sealed class TransportRegistryGatewayAdapter(
        Biolog.Features.Transport.Domain.IRouteRepository routeRepository,
        Biolog.Features.Transport.Domain.IVehicleRepository vehicleRepository,
        Biolog.Features.Transport.Domain.IStopRepository stopRepository,
        ITenantContextAccessor tenantAccessor) : ITransportRegistryGateway
    {
        public async Task<TransportRouteInfo?> GetRouteAsync(string routeId, CancellationToken cancellationToken = default)
        {
            var tenantId = RequireTenant();
            var route = await routeRepository.GetAsync(tenantId, routeId, cancellationToken);
            return route is null
                ? null
                : new TransportRouteInfo(route.Id, route.Status == Biolog.Features.Transport.Domain.RouteStatus.Active, [.. route.Stops.Select(s => s.StopId)]);
        }

        public async Task<bool> VehicleIsActiveAsync(string vehicleId, CancellationToken cancellationToken = default)
        {
            var tenantId = RequireTenant();
            var vehicle = await vehicleRepository.GetAsync(tenantId, vehicleId, cancellationToken);
            return vehicle is not null && vehicle.Status == Biolog.Features.Transport.Domain.VehicleStatus.Active;
        }

        public async Task<bool> StopIsActiveAsync(string stopId, CancellationToken cancellationToken = default)
        {
            var tenantId = RequireTenant();
            var stop = await stopRepository.GetAsync(tenantId, stopId, cancellationToken);
            return stop is not null && stop.Status == Biolog.Features.Transport.Domain.StopStatus.Active;
        }

        private string RequireTenant() => tenantAccessor.Context?.TenantId ?? throw TransportEventError.TenantContextMissing();
    }

    /// <summary>
    /// AC05's demo-seed write, registered Singleton (see <see cref="ITransportDemoSeedProvisioner"/>'s
    /// own header for why this is a SEPARATE class/interface from <see cref="TransportRegistryGatewayAdapter"/>,
    /// not one more method on it - a captive-dependency bug this codebase's own BIO-IDN-001 lesson
    /// already closed once). Depends only on the Singleton repositories, takes
    /// <c>tenantId</c> explicitly (the demo bridge's own fixed constant) - no
    /// <see cref="ITenantContextAccessor"/> dependency at all, so no captive-dependency risk.
    /// Mints a real Route/Stop/Vehicle through BIO-TRN-001's own aggregate factories
    /// (<c>RouteRecord.Create</c>/<c>StopRecord.Create</c>/<c>VehicleRecord.Create</c>, all
    /// <c>internal static</c>, reachable only because of this assembly's own
    /// <c>InternalsVisibleTo</c> grant) the first time either story's demo bridge needs one, found
    /// by its fixed name thereafter - idempotent, safe to call from either bridge, in either order.
    /// </summary>
    private sealed class TransportDemoSeedProvisioner(
        Biolog.Features.Transport.Domain.IRouteRepository routeRepository,
        Biolog.Features.Transport.Domain.IVehicleRepository vehicleRepository,
        Biolog.Features.Transport.Domain.IStopRepository stopRepository) : ITransportDemoSeedProvisioner
    {
        private const string DemoRouteName = "Route A — Riverside Loop"; // matches BIO-TRN-001's own bridge constant exactly - same shared demo route, found or created by whichever bridge runs first.
        private const string DemoStopName = "Riverside Gate";
        private const string DemoVehicleName = "Bus 01";
        private const string DemoActorId = "svc_transport_events_ui_bridge";

        private readonly SemaphoreSlim _demoSeedGate = new(1, 1);

        public async Task<TransportDemoSeed> EnsureDemoSeedAsync(string tenantId, CancellationToken cancellationToken = default)
        {
            await _demoSeedGate.WaitAsync(cancellationToken);
            try
            {
                var routes = await routeRepository.ListByTenantAsync(tenantId, cancellationToken);
                var route = routes.FirstOrDefault(r => r.Name == DemoRouteName && r.Status == Biolog.Features.Transport.Domain.RouteStatus.Active);

                if (route is not null)
                {
                    var existingStops = await stopRepository.ListByTenantAsync(tenantId, cancellationToken);
                    var existingStop = existingStops.FirstOrDefault(s => s.Name == DemoStopName) ?? existingStops.First(s => route.Stops.Any(rs => rs.StopId == s.Id));
                    var existingVehicles = await vehicleRepository.ListByTenantAsync(tenantId, cancellationToken);
                    var existingVehicle = existingVehicles.FirstOrDefault(v => v.Name == DemoVehicleName) ?? existingVehicles.First();
                    return new TransportDemoSeed(route.Id, existingStop.Id, existingVehicle.Id);
                }

                var now = Biolog.Features.Transport.Identifiers.UtcInstant.Now();

                var stop = Biolog.Features.Transport.Domain.StopRecord.Create(
                    tenantId, campusId: null, DemoStopName, "Main pedestrian gate on the riverside loop", DemoActorId, now);
                stop = await stopRepository.CreateAsync(stop, cancellationToken);

                var newRoute = Biolog.Features.Transport.Domain.RouteRecord.Create(
                    tenantId, campusId: null, DemoRouteName,
                    [new Biolog.Features.Transport.Domain.RouteStopEntry { StopId = stop.Id, SequenceOrder = 1, EstimatedOffsetMinutes = 0 }],
                    DemoActorId, now);
                newRoute = await routeRepository.CreateAsync(newRoute, cancellationToken);
                var activated = newRoute.Activate(DemoActorId, Biolog.Features.Transport.Identifiers.UtcInstant.Now());
                newRoute = await routeRepository.ReplaceAsync(newRoute, activated, cancellationToken);

                var vehicle = Biolog.Features.Transport.Domain.VehicleRecord.Create(
                    tenantId, campusId: null, DemoVehicleName, capacity: 40, deviceId: null, DemoActorId, now);
                vehicle = await vehicleRepository.CreateAsync(vehicle, cancellationToken);

                return new TransportDemoSeed(newRoute.Id, stop.Id, vehicle.Id);
            }
            finally
            {
                _demoSeedGate.Release();
            }
        }
    }
}

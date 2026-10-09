using Biolog.Features.Transport.Api;
using Biolog.Features.Transport.Audit;
using Biolog.Features.Transport.Domain;
using Xunit;

namespace Biolog.Features.Transport.Tests;

/// <summary>AC01 (versioned lifecycle) and AC03 (vehicle/device linkage) against <see cref="RouteService"/>/<see cref="VehicleService"/>/<see cref="StopService"/>.</summary>
public class RegistryServiceTests
{
    private const string TenantId = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77";
    private const string DeviceId = "dev_0199e0c2-1b24-7085-9c36-4e9f6a8b2d03";
    private static readonly AuditActor Actor = new(ActorType.User, "usr-transport-admin-1");

    private static StopService MakeStopService(out InMemoryStopRepository repository)
    {
        repository = new InMemoryStopRepository();
        return new StopService(new FakeCurrentTenantProvider(TenantId), repository, new InMemoryTransportAuditSink(), new StopLockRegistry());
    }

    private static RouteService MakeRouteService(out InMemoryRouteRepository repository, IStopRepository? stopRepository = null)
    {
        repository = new InMemoryRouteRepository();
        return new RouteService(new FakeCurrentTenantProvider(TenantId), repository, stopRepository ?? new InMemoryStopRepository(), new InMemoryTransportAuditSink(), new RouteLockRegistry());
    }

    private static VehicleService MakeVehicleService(out InMemoryVehicleRepository repository, IDeviceExistenceGateway? deviceGateway = null)
    {
        repository = new InMemoryVehicleRepository();
        return new VehicleService(new FakeCurrentTenantProvider(TenantId), repository, new InMemoryTransportAuditSink(), new VehicleLockRegistry(), deviceGateway ?? new FakeDeviceExistenceGateway());
    }

    // --- AC01: Stop ---

    [Fact]
    public async Task AC01_a_a_stop_is_created_active_at_version_1()
    {
        var service = MakeStopService(out _);
        var stop = await service.CreateAsync(new CreateStopRequest { Name = "Riverside Gate" }, Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f25");

        Assert.Equal(StopStatus.Active, stop.Status);
        Assert.Equal(1, stop.Version);
    }

    [Fact]
    public async Task AC01_b_a_retired_stop_cannot_be_retired_again()
    {
        var service = MakeStopService(out _);
        var stop = await service.CreateAsync(new CreateStopRequest { Name = "Riverside Gate" }, Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f25");
        stop = await service.RetireAsync(stop.Id, new VersionedTransportRequest { Version = stop.Version }, Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f26");

        var ex = await Assert.ThrowsAsync<TransportError>(() =>
            service.RetireAsync(stop.Id, new VersionedTransportRequest { Version = stop.Version }, Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f27"));
        Assert.Equal("invalid_status_transition", ex.Code);
    }

    // --- AC01: Route ---

    [Fact]
    public async Task AC01_c_a_route_is_created_draft_and_activates_exactly_once()
    {
        var stopRepository = new InMemoryStopRepository();
        var stopService = MakeStopServiceOver(stopRepository);
        var stop = await stopService.CreateAsync(new CreateStopRequest { Name = "Riverside Gate" }, Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f25");

        var routeService = MakeRouteService(out var routeRepository, stopRepository);
        var route = await routeService.CreateAsync(
            new CreateRouteRequest { Name = "Route A", Stops = [new RouteStopEntryRequest { StopId = stop.Id, SequenceOrder = 1 }] },
            Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f26");

        Assert.Equal(RouteStatus.Draft, route.Status);
        Assert.Single(route.Stops);

        route = await routeService.ActivateAsync(route.Id, new VersionedTransportRequest { Version = route.Version }, Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f27");
        Assert.Equal(RouteStatus.Active, route.Status);

        var ex = await Assert.ThrowsAsync<TransportError>(() =>
            routeService.ActivateAsync(route.Id, new VersionedTransportRequest { Version = route.Version }, Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f28"));
        Assert.Equal("invalid_status_transition", ex.Code);

        var reread = await routeRepository.GetAsync(TenantId, route.Id);
        Assert.Equal(RouteStatus.Active, reread!.Status); // untouched by the failed re-activate
    }

    [Fact]
    public async Task AC01_d_a_route_referencing_a_nonexistent_stop_is_rejected()
    {
        var routeService = MakeRouteService(out _);
        var ex = await Assert.ThrowsAsync<TransportError>(() =>
            routeService.CreateAsync(
                new CreateRouteRequest { Name = "Route A", Stops = [new RouteStopEntryRequest { StopId = "not-a-real-stop", SequenceOrder = 1 }] },
                Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f25"));
        Assert.Equal("stop_not_found", ex.Code);
    }

    [Fact]
    public async Task AC01_e_a_route_referencing_a_retired_stop_is_rejected()
    {
        var stopRepository = new InMemoryStopRepository();
        var stopService = MakeStopServiceOver(stopRepository);
        var stop = await stopService.CreateAsync(new CreateStopRequest { Name = "Riverside Gate" }, Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f25");
        await stopService.RetireAsync(stop.Id, new VersionedTransportRequest { Version = stop.Version }, Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f26");

        var routeService = MakeRouteService(out _, stopRepository);
        var ex = await Assert.ThrowsAsync<TransportError>(() =>
            routeService.CreateAsync(
                new CreateRouteRequest { Name = "Route A", Stops = [new RouteStopEntryRequest { StopId = stop.Id, SequenceOrder = 1 }] },
                Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f27"));
        Assert.Equal("stop_not_active", ex.Code);
    }

    // --- AC01/AC03: Vehicle ---

    [Fact]
    public async Task AC01_f_a_vehicle_is_created_active_and_cycles_out_of_service_and_back()
    {
        var service = MakeVehicleService(out var repository);
        var vehicle = await service.CreateAsync(new CreateVehicleRequest { Name = "Bus 01" }, Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f25");
        Assert.Equal(VehicleStatus.Active, vehicle.Status);

        vehicle = await service.SetOutOfServiceAsync(vehicle.Id, new VersionedTransportRequest { Version = vehicle.Version }, Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f26");
        Assert.Equal(VehicleStatus.OutOfService, vehicle.Status);

        vehicle = await service.ReactivateAsync(vehicle.Id, new VersionedTransportRequest { Version = vehicle.Version }, Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f27");
        Assert.Equal(VehicleStatus.Active, vehicle.Status);

        vehicle = await service.RetireAsync(vehicle.Id, new VersionedTransportRequest { Version = vehicle.Version }, Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f28");
        Assert.Equal(VehicleStatus.Retired, vehicle.Status);

        var ex = await Assert.ThrowsAsync<TransportError>(() =>
            service.ReactivateAsync(vehicle.Id, new VersionedTransportRequest { Version = vehicle.Version }, Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f29"));
        Assert.Equal("invalid_status_transition", ex.Code); // AC01: Retired is terminal, even though Active<->OutOfService otherwise cycles
    }

    [Fact]
    public async Task AC03_a_a_device_id_that_does_not_exist_is_rejected()
    {
        var service = MakeVehicleService(out _, new FakeDeviceExistenceGateway(new HashSet<string>()));
        var ex = await Assert.ThrowsAsync<TransportError>(() =>
            service.CreateAsync(new CreateVehicleRequest { Name = "Bus 01", DeviceId = DeviceId }, Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f25"));
        Assert.Equal("device_not_found", ex.Code);
    }

    [Fact]
    public async Task AC03_b_a_device_id_that_exists_links_successfully_and_is_not_status_gated()
    {
        // FakeDeviceExistenceGateway's default (every device exists) stands in for a device that
        // exists but is still PendingActivation - the gateway is existence-only (see
        // Domain/IDeviceExistenceGateway.cs's own header), so this must still succeed.
        var service = MakeVehicleService(out var repository);
        var vehicle = await service.CreateAsync(new CreateVehicleRequest { Name = "Bus 01", DeviceId = DeviceId }, Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f25");

        Assert.Equal(DeviceId, vehicle.DeviceId);
        var reread = await repository.GetAsync(TenantId, vehicle.Id);
        Assert.Equal(DeviceId, reread!.DeviceId);
    }

    private static StopService MakeStopServiceOver(IStopRepository repository) =>
        new(new FakeCurrentTenantProvider(TenantId), repository, new InMemoryTransportAuditSink(), new StopLockRegistry());
}

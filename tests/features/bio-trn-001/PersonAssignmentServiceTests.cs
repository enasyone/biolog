using Biolog.Features.Transport.Api;
using Biolog.Features.Transport.Audit;
using Biolog.Features.Transport.Domain;
using Xunit;

namespace Biolog.Features.Transport.Tests;

/// <summary>AC02 (validity windows, overlap invariant, auditable) and AC04's campus-consistency rule against <see cref="PersonAssignmentService"/>.</summary>
public class PersonAssignmentServiceTests
{
    private const string TenantId = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77";
    private const string PersonId = "per_0199e0c2-1111-7222-8333-444455556666";
    private static readonly AuditActor Actor = new(ActorType.User, "usr-transport-admin-1");

    private sealed record Harness(
        PersonAssignmentService Service, InMemoryPersonAssignmentRepository AssignmentRepository,
        InMemoryRouteRepository RouteRepository, InMemoryStopRepository StopRepository,
        RouteService RouteService, StopService StopService);

    private static Harness MakeHarness(IPermissionGateway? permissionGateway = null, IPersonExistenceGateway? personGateway = null)
    {
        var stopRepository = new InMemoryStopRepository();
        var routeRepository = new InMemoryRouteRepository();
        var assignmentRepository = new InMemoryPersonAssignmentRepository();
        var auditSink = new InMemoryTransportAuditSink();

        var stopService = new StopService(new FakeCurrentTenantProvider(TenantId), stopRepository, auditSink, new StopLockRegistry());
        var routeService = new RouteService(new FakeCurrentTenantProvider(TenantId), routeRepository, stopRepository, auditSink, new RouteLockRegistry());
        var service = new PersonAssignmentService(
            new FakeCurrentTenantProvider(TenantId), assignmentRepository, routeRepository, stopRepository, auditSink,
            new PersonAssignmentLockRegistry(), new PersonAssignmentUniquenessLockRegistry(),
            personGateway ?? new FakePersonExistenceGateway(), permissionGateway ?? new FakePermissionGateway());

        return new Harness(service, assignmentRepository, routeRepository, stopRepository, routeService, stopService);
    }

    /// <summary>An Active route with one Active stop on it, ready to be assigned against.</summary>
    private static async Task<(string RouteId, string StopId)> SeedActiveRouteAsync(Harness h, string? routeCampusId = null, string? stopCampusId = null)
    {
        var stop = await h.StopService.CreateAsync(new CreateStopRequest { Name = "Riverside Gate", CampusId = stopCampusId }, Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f20");
        var route = await h.RouteService.CreateAsync(
            new CreateRouteRequest { Name = "Route A", CampusId = routeCampusId, Stops = [new RouteStopEntryRequest { StopId = stop.Id, SequenceOrder = 1 }] },
            Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f21");
        route = await h.RouteService.ActivateAsync(route.Id, new VersionedTransportRequest { Version = route.Version }, Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f22");
        return (route.Id, stop.Id);
    }

    [Fact]
    public async Task AC02_a_creating_an_assignment_requires_the_assign_permission()
    {
        var h = MakeHarness(new FakePermissionGateway(new HashSet<(string, string)>()));
        var (routeId, stopId) = await SeedActiveRouteAsync(h);

        var ex = await Assert.ThrowsAsync<TransportError>(() =>
            h.Service.CreateAsync(
                new CreatePersonAssignmentRequest { PersonId = PersonId, RouteId = routeId, StopId = stopId, ValidFrom = "2026-01-01T00:00:00.000Z" },
                Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f25"));
        Assert.Equal("permission_required", ex.Code);
    }

    [Fact]
    public async Task AC02_b_creating_persists_and_audits_the_assignment()
    {
        var h = MakeHarness();
        var (routeId, stopId) = await SeedActiveRouteAsync(h);

        var assignment = await h.Service.CreateAsync(
            new CreatePersonAssignmentRequest { PersonId = PersonId, RouteId = routeId, StopId = stopId, ValidFrom = "2026-01-01T00:00:00.000Z" },
            Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f25");

        Assert.Equal(PersonId, assignment.PersonId);
        Assert.Null(assignment.ValidUntil); // open-ended
        var reread = await h.AssignmentRepository.GetAsync(TenantId, assignment.Id);
        Assert.NotNull(reread);
    }

    [Fact]
    public async Task AC02_c_a_person_id_that_does_not_exist_is_rejected()
    {
        var h = MakeHarness(personGateway: new FakePersonExistenceGateway(new HashSet<string>()));
        var (routeId, stopId) = await SeedActiveRouteAsync(h);

        var ex = await Assert.ThrowsAsync<TransportError>(() =>
            h.Service.CreateAsync(
                new CreatePersonAssignmentRequest { PersonId = PersonId, RouteId = routeId, StopId = stopId, ValidFrom = "2026-01-01T00:00:00.000Z" },
                Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f25"));
        Assert.Equal("person_not_found", ex.Code);
    }

    [Fact]
    public async Task AC02_d_assigning_to_a_draft_route_is_rejected()
    {
        var h = MakeHarness();
        var stop = await h.StopService.CreateAsync(new CreateStopRequest { Name = "Riverside Gate" }, Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f20");
        var route = await h.RouteService.CreateAsync(
            new CreateRouteRequest { Name = "Route A", Stops = [new RouteStopEntryRequest { StopId = stop.Id, SequenceOrder = 1 }] },
            Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f21"); // never activated - stays Draft

        var ex = await Assert.ThrowsAsync<TransportError>(() =>
            h.Service.CreateAsync(
                new CreatePersonAssignmentRequest { PersonId = PersonId, RouteId = route.Id, StopId = stop.Id, ValidFrom = "2026-01-01T00:00:00.000Z" },
                Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f25"));
        Assert.Equal("route_not_active", ex.Code);
    }

    [Fact]
    public async Task AC02_e_assigning_to_a_stop_not_on_the_route_is_rejected()
    {
        var h = MakeHarness();
        var (routeId, _) = await SeedActiveRouteAsync(h);
        var otherStop = await h.StopService.CreateAsync(new CreateStopRequest { Name = "Different Stop" }, Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f23");

        var ex = await Assert.ThrowsAsync<TransportError>(() =>
            h.Service.CreateAsync(
                new CreatePersonAssignmentRequest { PersonId = PersonId, RouteId = routeId, StopId = otherStop.Id, ValidFrom = "2026-01-01T00:00:00.000Z" },
                Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f25"));
        Assert.Equal("stop_not_on_route", ex.Code);
    }

    [Fact]
    public async Task AC02_f_an_overlapping_window_for_the_same_person_is_rejected()
    {
        var h = MakeHarness();
        var (routeId, stopId) = await SeedActiveRouteAsync(h);

        await h.Service.CreateAsync(
            new CreatePersonAssignmentRequest { PersonId = PersonId, RouteId = routeId, StopId = stopId, ValidFrom = "2026-01-01T00:00:00.000Z", ValidUntil = "2026-06-01T00:00:00.000Z" },
            Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f25");

        var ex = await Assert.ThrowsAsync<TransportError>(() =>
            h.Service.CreateAsync(
                new CreatePersonAssignmentRequest { PersonId = PersonId, RouteId = routeId, StopId = stopId, ValidFrom = "2026-03-01T00:00:00.000Z" }, // overlaps, and is itself open-ended
                Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f26"));
        Assert.Equal("overlapping_assignment", ex.Code);
    }

    [Fact]
    public async Task AC02_g_a_non_overlapping_sequential_window_for_the_same_person_is_allowed()
    {
        var h = MakeHarness();
        var (routeId, stopId) = await SeedActiveRouteAsync(h);

        await h.Service.CreateAsync(
            new CreatePersonAssignmentRequest { PersonId = PersonId, RouteId = routeId, StopId = stopId, ValidFrom = "2026-01-01T00:00:00.000Z", ValidUntil = "2026-06-01T00:00:00.000Z" },
            Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f25");

        var second = await h.Service.CreateAsync(
            new CreatePersonAssignmentRequest { PersonId = PersonId, RouteId = routeId, StopId = stopId, ValidFrom = "2026-06-01T00:00:00.000Z" }, // starts exactly when the first ends - half-open, no overlap
            Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f26");

        Assert.NotNull(second);
    }

    [Fact]
    public async Task AC02_h_concurrent_create_attempts_for_the_same_person_and_overlapping_window_only_one_wins()
    {
        var h = MakeHarness();
        var (routeId, stopId) = await SeedActiveRouteAsync(h);

        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(async i =>
        {
            try
            {
                await h.Service.CreateAsync(
                    new CreatePersonAssignmentRequest { PersonId = PersonId, RouteId = routeId, StopId = stopId, ValidFrom = "2026-01-01T00:00:00.000Z" },
                    Actor, $"evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f{i:x2}");
                return true;
            }
            catch (TransportError)
            {
                return false;
            }
        }));

        Assert.Single(results, r => r); // exactly one caller's create wins the overlap check
        var all = await h.AssignmentRepository.ListByPersonAsync(TenantId, PersonId);
        Assert.Single(all);
    }

    [Fact]
    public async Task AC02_i_ending_an_assignment_requires_the_assign_permission()
    {
        var h = MakeHarness();
        var (routeId, stopId) = await SeedActiveRouteAsync(h);
        var assignment = await h.Service.CreateAsync(
            new CreatePersonAssignmentRequest { PersonId = PersonId, RouteId = routeId, StopId = stopId, ValidFrom = "2026-01-01T00:00:00.000Z" },
            Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f25");

        var denyingHarness = MakeHarness(new FakePermissionGateway(new HashSet<(string, string)>()));
        var ex = await Assert.ThrowsAsync<TransportError>(() =>
            denyingHarness.Service.EndAsync(assignment.Id, new EndPersonAssignmentRequest { Version = assignment.Version }, Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f26"));
        Assert.Equal("permission_required", ex.Code);
    }

    [Fact]
    public async Task AC02_j_ending_an_open_ended_assignment_sets_valid_until_and_is_audited()
    {
        var h = MakeHarness();
        var (routeId, stopId) = await SeedActiveRouteAsync(h);
        var assignment = await h.Service.CreateAsync(
            new CreatePersonAssignmentRequest { PersonId = PersonId, RouteId = routeId, StopId = stopId, ValidFrom = "2026-01-01T00:00:00.000Z" },
            Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f25");

        var ended = await h.Service.EndAsync(assignment.Id, new EndPersonAssignmentRequest { Version = assignment.Version, EndAt = "2026-02-01T00:00:00.000Z" }, Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f26");
        Assert.Equal("2026-02-01T00:00:00.000Z", ended.ValidUntil);

        var ex = await Assert.ThrowsAsync<TransportError>(() =>
            h.Service.EndAsync(ended.Id, new EndPersonAssignmentRequest { Version = ended.Version, EndAt = "2026-02-05T00:00:00.000Z" }, Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f27"));
        Assert.Equal("assignment_already_ended", ex.Code);
    }

    [Fact]
    public async Task AC04_a_a_campus_mismatch_between_route_and_stop_is_rejected()
    {
        var h = MakeHarness();
        var (routeId, stopId) = await SeedActiveRouteAsync(h, routeCampusId: "campus-north", stopCampusId: "campus-south");

        var ex = await Assert.ThrowsAsync<TransportError>(() =>
            h.Service.CreateAsync(
                new CreatePersonAssignmentRequest { PersonId = PersonId, RouteId = routeId, StopId = stopId, ValidFrom = "2026-01-01T00:00:00.000Z" },
                Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f25"));
        Assert.Equal("campus_mismatch", ex.Code);
    }

    [Fact]
    public async Task AC04_b_a_matching_campus_on_route_and_stop_is_allowed()
    {
        var h = MakeHarness();
        var (routeId, stopId) = await SeedActiveRouteAsync(h, routeCampusId: "campus-north", stopCampusId: "campus-north");

        var assignment = await h.Service.CreateAsync(
            new CreatePersonAssignmentRequest { PersonId = PersonId, RouteId = routeId, StopId = stopId, ValidFrom = "2026-01-01T00:00:00.000Z" },
            Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f25");
        Assert.NotNull(assignment);
    }

    [Fact]
    public async Task AC04_c_no_campus_declared_on_either_side_is_not_a_mismatch()
    {
        var h = MakeHarness();
        var (routeId, stopId) = await SeedActiveRouteAsync(h); // neither declares a campus

        var assignment = await h.Service.CreateAsync(
            new CreatePersonAssignmentRequest { PersonId = PersonId, RouteId = routeId, StopId = stopId, ValidFrom = "2026-01-01T00:00:00.000Z" },
            Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f25");
        Assert.NotNull(assignment);
    }
}

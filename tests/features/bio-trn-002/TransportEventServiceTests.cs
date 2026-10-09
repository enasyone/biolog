using Biolog.Features.TransportEvents.Api;
using Biolog.Features.TransportEvents.Audit;
using Biolog.Features.TransportEvents.Domain;
using Xunit;

namespace Biolog.Features.TransportEvents.Tests;

/// <summary>AC01 (capture shape), AC02 (idempotent capture) against <see cref="TransportEventService"/>.</summary>
public class TransportEventServiceTests
{
    private const string TenantId = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77";
    private const string PersonId = "per_0199e0c2-1111-7222-8333-444455556666";
    private const string RouteId = "route-A";
    private const string VehicleId = "bus-01";
    private const string StopId = "stop-1";
    private static readonly AuditActor Actor = new(ActorType.User, "usr-reader-1");

    private sealed record Harness(
        TransportEventService Service, InMemoryTransportEventRepository EventRepository,
        InMemoryTriggeredNotificationLog NotificationLog, InMemoryTransportNotificationConfigRepository ConfigRepository,
        FakeTransportRegistryGateway RegistryGateway);

    private static Harness MakeHarness(IPersonExistenceGateway? personGateway = null, bool seedActiveRoute = true)
    {
        var eventRepository = new InMemoryTransportEventRepository();
        var configRepository = new InMemoryTransportNotificationConfigRepository();
        var notificationLog = new InMemoryTriggeredNotificationLog();
        var auditSink = new InMemoryTransportEventAuditSink();
        var registryGateway = new FakeTransportRegistryGateway();

        if (seedActiveRoute)
        {
            registryGateway.Routes[RouteId] = new TransportRouteInfo(RouteId, true, [StopId]);
            registryGateway.ActiveVehicleIds.Add(VehicleId);
            registryGateway.ActiveStopIds.Add(StopId);
        }

        var service = new TransportEventService(
            new FakeCurrentTenantProvider(TenantId), eventRepository, auditSink, new TransportEventLockRegistry(),
            personGateway ?? new FakePersonExistenceGateway(), registryGateway, configRepository, notificationLog);

        return new Harness(service, eventRepository, notificationLog, configRepository, registryGateway);
    }

    private static CaptureTransportEventRequest BoardRequest(string idempotencyKey, string occurredAt = "2026-01-01T08:00:00.000Z", string? stopId = StopId) => new()
    {
        PersonId = PersonId,
        RouteId = RouteId,
        VehicleId = VehicleId,
        StopId = stopId,
        Mode = "board",
        Evidence = [new TransportEvidenceEntryRequest { Method = "uhf", Verified = true, Confidence = 0.9 }],
        OccurredAt = occurredAt,
        IdempotencyKey = idempotencyKey,
    };

    [Fact]
    public async Task AC01_a_capturing_a_board_event_persists_route_vehicle_stop_and_evidence()
    {
        var h = MakeHarness();

        var captured = await h.Service.CaptureAsync(BoardRequest("idem-1"), Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f20");

        Assert.Equal(PersonId, captured.PersonId);
        Assert.Equal(RouteId, captured.RouteId);
        Assert.Equal(VehicleId, captured.VehicleId);
        Assert.Equal(StopId, captured.StopId);
        Assert.Equal(TransportEventMode.Board, captured.Mode);
        Assert.Single(captured.Evidence);
        Assert.Equal(EvidenceMethod.Uhf, captured.Evidence[0].Method);
        var reread = await h.EventRepository.GetByIdempotencyKeyAsync(TenantId, "idem-1");
        Assert.NotNull(reread);
    }

    [Fact]
    public async Task AC01_b_at_least_one_evidence_entry_is_required()
    {
        var h = MakeHarness();
        var request = BoardRequest("idem-2") with { Evidence = [] };

        var ex = await Assert.ThrowsAsync<TransportEventError>(() => h.Service.CaptureAsync(request, Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f21"));
        Assert.Equal("validation_failed", ex.Code);
    }

    [Fact]
    public async Task AC01_c_a_route_that_does_not_exist_is_rejected()
    {
        var h = MakeHarness();
        var request = BoardRequest("idem-3") with { RouteId = "no-such-route" };

        var ex = await Assert.ThrowsAsync<TransportEventError>(() => h.Service.CaptureAsync(request, Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f22"));
        Assert.Equal("route_not_found", ex.Code);
    }

    [Fact]
    public async Task AC01_d_a_draft_or_retired_route_is_rejected()
    {
        var h = MakeHarness(seedActiveRoute: false);
        h.RegistryGateway.Routes[RouteId] = new TransportRouteInfo(RouteId, false, [StopId]);
        h.RegistryGateway.ActiveVehicleIds.Add(VehicleId);
        h.RegistryGateway.ActiveStopIds.Add(StopId);

        var ex = await Assert.ThrowsAsync<TransportEventError>(() => h.Service.CaptureAsync(BoardRequest("idem-4"), Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f23"));
        Assert.Equal("route_not_active", ex.Code);
    }

    [Fact]
    public async Task AC01_e_an_out_of_service_vehicle_is_rejected()
    {
        var h = MakeHarness(seedActiveRoute: false);
        h.RegistryGateway.Routes[RouteId] = new TransportRouteInfo(RouteId, true, [StopId]);
        h.RegistryGateway.ActiveStopIds.Add(StopId);
        // Vehicle deliberately NOT added to ActiveVehicleIds.

        var ex = await Assert.ThrowsAsync<TransportEventError>(() => h.Service.CaptureAsync(BoardRequest("idem-5"), Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f24"));
        Assert.Equal("vehicle_not_active", ex.Code);
    }

    [Fact]
    public async Task AC01_f_a_stop_not_on_the_target_route_is_rejected()
    {
        var h = MakeHarness();
        var request = BoardRequest("idem-6", stopId: "some-other-stop");

        var ex = await Assert.ThrowsAsync<TransportEventError>(() => h.Service.CaptureAsync(request, Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f25"));
        Assert.Equal("stop_not_on_route", ex.Code);
    }

    [Fact]
    public async Task AC01_g_a_stop_on_the_route_but_itself_retired_is_rejected()
    {
        var h = MakeHarness(seedActiveRoute: false);
        h.RegistryGateway.Routes[RouteId] = new TransportRouteInfo(RouteId, true, [StopId]);
        h.RegistryGateway.ActiveVehicleIds.Add(VehicleId);
        // StopId deliberately NOT added to ActiveStopIds - on the route, but not itself active.

        var ex = await Assert.ThrowsAsync<TransportEventError>(() => h.Service.CaptureAsync(BoardRequest("idem-7"), Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f26"));
        Assert.Equal("stop_not_active", ex.Code);
    }

    [Fact]
    public async Task AC01_h_a_person_id_that_does_not_exist_is_rejected()
    {
        var h = MakeHarness(personGateway: new FakePersonExistenceGateway(new HashSet<string>()));

        var ex = await Assert.ThrowsAsync<TransportEventError>(() => h.Service.CaptureAsync(BoardRequest("idem-8"), Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f27"));
        Assert.Equal("person_not_found", ex.Code);
    }

    [Fact]
    public async Task AC02_a_replaying_the_identical_idempotency_key_with_identical_content_answers_idempotently_no_new_event()
    {
        var h = MakeHarness();
        var request = BoardRequest("idem-9");

        var first = await h.Service.CaptureAsync(request, Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f28");
        var second = await h.Service.CaptureAsync(request, Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f29");

        Assert.Equal(first.EventId, second.EventId);
        var all = await h.EventRepository.ListForTenantAsync(TenantId);
        Assert.Single(all);
    }

    [Fact]
    public async Task AC02_b_reusing_the_same_idempotency_key_with_different_content_is_a_conflict()
    {
        var h = MakeHarness();
        await h.Service.CaptureAsync(BoardRequest("idem-10"), Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f30");
        var differentContent = BoardRequest("idem-10") with { Mode = "alight" };

        var ex = await Assert.ThrowsAsync<TransportEventError>(() => h.Service.CaptureAsync(differentContent, Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f31"));
        Assert.Equal("idempotency_conflict", ex.Code);
    }

    [Fact]
    public async Task AC02_c_20_concurrent_captures_with_the_same_idempotency_key_all_converge_on_exactly_one_stored_event()
    {
        // A genuine concurrency race, not merely sequential calls - the same discipline BIO-ATT-001's
        // own 20-way capture race test establishes: the per-(tenant, idempotency_key) lock must
        // actually serialize concurrent callers, not just look correct when called one at a time.
        var h = MakeHarness();
        var request = BoardRequest("idem-race");

        var tasks = Enumerable.Range(0, 20).Select(_ => h.Service.CaptureAsync(request, Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f32"));
        var results = await Task.WhenAll(tasks);

        Assert.Equal(20, results.Length);
        Assert.Single(results.Select(r => r.EventId).Distinct());
        var all = await h.EventRepository.ListForTenantAsync(TenantId);
        Assert.Single(all);
    }

    [Fact]
    public async Task AC02_d_a_board_event_can_omit_stop_id_in_favour_of_a_free_text_location()
    {
        var h = MakeHarness();
        var request = BoardRequest("idem-11", stopId: null) with { Location = "Front door, curb side" };

        var captured = await h.Service.CaptureAsync(request, Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f33");

        Assert.Null(captured.StopId);
        Assert.Equal("Front door, curb side", captured.Location);
    }
}

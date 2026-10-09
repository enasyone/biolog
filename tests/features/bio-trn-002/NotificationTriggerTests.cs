using Biolog.Features.TransportEvents.Api;
using Biolog.Features.TransportEvents.Audit;
using Biolog.Features.TransportEvents.Domain;
using Xunit;

namespace Biolog.Features.TransportEvents.Tests;

/// <summary>
/// AC04 end to end: a capture that reveals a genuine missing-alight exception (a legitimate,
/// already-backdated <c>occurred_at</c> - not a clock trick, see <see cref="TransportEventService"/>'s
/// own header) triggers a real, readable-back <see cref="TriggeredNotification"/> when the tenant's
/// config enables it, and is deduplicated on replay.
/// </summary>
public class NotificationTriggerTests
{
    private const string TenantId = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77";
    private const string PersonId = "per_0199e0c2-1111-7222-8333-444455556666";
    private const string RouteId = "route-A";
    private const string VehicleId = "bus-01";
    private static readonly AuditActor Actor = new(ActorType.User, "usr-reader-1");

    private sealed record Harness(TransportEventService EventService, TransportNotificationConfigService ConfigService, InMemoryTriggeredNotificationLog NotificationLog);

    private static Harness MakeHarness()
    {
        var eventRepository = new InMemoryTransportEventRepository();
        var configRepository = new InMemoryTransportNotificationConfigRepository();
        var notificationLog = new InMemoryTriggeredNotificationLog();
        var auditSink = new InMemoryTransportEventAuditSink();
        var registryGateway = new FakeTransportRegistryGateway();
        registryGateway.Routes[RouteId] = new TransportRouteInfo(RouteId, true, []);
        registryGateway.ActiveVehicleIds.Add(VehicleId);

        var eventService = new TransportEventService(
            new FakeCurrentTenantProvider(TenantId), eventRepository, auditSink, new TransportEventLockRegistry(),
            new FakePersonExistenceGateway(), registryGateway, configRepository, notificationLog);
        var configService = new TransportNotificationConfigService(
            new FakeCurrentTenantProvider(TenantId), configRepository, auditSink, new TransportNotificationConfigLockRegistry());

        return new Harness(eventService, configService, notificationLog);
    }

    private static CaptureTransportEventRequest OldUnmatchedBoardRequest(string idempotencyKey) => new()
    {
        PersonId = PersonId,
        RouteId = RouteId,
        VehicleId = VehicleId,
        StopId = null,
        Mode = "board",
        Evidence = [new TransportEvidenceEntryRequest { Method = "uhf", Verified = true, Confidence = 0.9 }],
        OccurredAt = DateTime.UtcNow.AddHours(-20).ToString("yyyy-MM-ddTHH:mm:ss.fffZ"), // well past the 12-hour exception window, but still today or yesterday - a legitimate late/backfilled capture
        IdempotencyKey = idempotencyKey,
    };

    [Fact]
    public async Task AC04_a_a_capture_that_is_already_a_missing_alight_exception_triggers_a_notification_when_enabled()
    {
        var h = MakeHarness();
        await h.ConfigService.SetAsync(new SetTransportNotificationConfigRequest { NotifyOnMissingAlightException = true }, Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f20");

        await h.EventService.CaptureAsync(OldUnmatchedBoardRequest("idem-trigger-1"), Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f21");

        var notifications = await h.NotificationLog.ListForTenantAsync(TenantId);
        Assert.Single(notifications);
        Assert.Equal(PersonId, notifications[0].PersonId);
        Assert.Equal("missing_alight_exception", notifications[0].RuleId);
    }

    [Fact]
    public async Task AC04_b_the_same_exception_does_not_trigger_when_the_tenant_has_not_enabled_it_the_default()
    {
        var h = MakeHarness();
        // No SetAsync call - config stays at its real, non-null default (disabled).

        await h.EventService.CaptureAsync(OldUnmatchedBoardRequest("idem-trigger-2"), Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f22");

        var notifications = await h.NotificationLog.ListForTenantAsync(TenantId);
        Assert.Empty(notifications);
    }

    [Fact]
    public async Task AC04_c_a_recent_unmatched_board_does_not_trigger_it_is_not_an_exception_yet()
    {
        var h = MakeHarness();
        await h.ConfigService.SetAsync(new SetTransportNotificationConfigRequest { NotifyOnMissingAlightException = true }, Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f23");
        var recentRequest = OldUnmatchedBoardRequest("idem-trigger-3") with { OccurredAt = DateTime.UtcNow.AddMinutes(-5).ToString("yyyy-MM-ddTHH:mm:ss.fffZ") };

        await h.EventService.CaptureAsync(recentRequest, Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f24");

        var notifications = await h.NotificationLog.ListForTenantAsync(TenantId);
        Assert.Empty(notifications);
    }

    [Fact]
    public async Task AC04_d_capturing_the_same_idempotency_key_twice_never_double_fires_the_notification()
    {
        var h = MakeHarness();
        await h.ConfigService.SetAsync(new SetTransportNotificationConfigRequest { NotifyOnMissingAlightException = true }, Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f25");
        var request = OldUnmatchedBoardRequest("idem-trigger-4");

        await h.EventService.CaptureAsync(request, Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f26");
        await h.EventService.CaptureAsync(request, Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f27"); // idempotent replay, AC02

        var notifications = await h.NotificationLog.ListForTenantAsync(TenantId);
        Assert.Single(notifications);
    }
}

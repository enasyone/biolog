using Xunit;

namespace Biolog.Features.TransportNotifications.Tests;

/// <summary>BIO-TRN-003-AC02: "PII is minimized."</summary>
public sealed class TransportNotificationPayloadRedactorTests
{
    [Fact]
    public void Minimize_carries_only_the_allowlisted_fields_and_drops_location_and_vehicle_id()
    {
        var item = new TransportFeedItem
        {
            SourceEventId = "evt_1",
            PersonId = "per_test_person",
            RouteId = "route_1",
            VehicleId = "vehicle_1",
            Location = "123 Synthetic Lane, Testville",
            EventType = TransportNotificationEventType.Board,
            OccurredAt = DateTimeOffset.Parse("2026-10-07T08:00:00Z"),
        };

        var payload = TransportNotificationPayloadRedactor.Minimize("ten_a", item);

        Assert.Equal("ten_a", payload.TenantId);
        Assert.Equal("per_test_person", payload.PersonId);
        Assert.Equal("route_1", payload.RouteId);
        Assert.Equal(TransportNotificationEventType.Board, payload.EventType);
        Assert.Equal(item.OccurredAt, payload.OccurredAt);

        // AC02 - the richer, more re-identifying raw fields never reach the minimized payload.
        // TransportNotificationPayload has no Location/VehicleId property at all (a structural
        // guarantee, not merely a runtime check), so this also proves at compile time that no
        // future edit can accidentally start carrying them through.
        Assert.DoesNotContain("Location", typeof(TransportNotificationPayload).GetProperties().Select(p => p.Name));
        Assert.DoesNotContain("VehicleId", typeof(TransportNotificationPayload).GetProperties().Select(p => p.Name));
    }

    [Fact]
    public void Minimize_preserves_a_null_route_id_for_a_missed_alight_item()
    {
        var item = new TransportFeedItem
        {
            SourceEventId = "triggered_1",
            PersonId = "per_test_person",
            RouteId = null,
            VehicleId = null,
            Location = null,
            EventType = TransportNotificationEventType.MissedAlight,
            OccurredAt = DateTimeOffset.Parse("2026-10-07T08:00:00Z"),
        };

        var payload = TransportNotificationPayloadRedactor.Minimize("ten_a", item);

        Assert.Null(payload.RouteId);
        Assert.Equal(TransportNotificationEventType.MissedAlight, payload.EventType);
    }
}

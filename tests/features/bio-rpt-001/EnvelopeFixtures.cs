using Biolog.Features.Notifications;

namespace Biolog.Features.Notifications.Tests;

/// <summary>Builds a synthetic, schema-shaped envelope for tests, matching
/// `contracts/schemas/event-envelope.schema.json`'s field shapes without needing a real
/// producer (none exists yet in this codebase - see docs/features/bio-rpt-001/README.md).</summary>
public static class EnvelopeFixtures
{
    public static DomainEventEnvelope Build(
        string tenantId = "ten_test-tenant",
        AggregateType aggregateType = AggregateType.Device,
        int schemaVersion = 1,
        EventClassification classification = EventClassification.Internal,
        IReadOnlyDictionary<string, string>? payload = null,
        string? eventId = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new DomainEventEnvelope(
            EventId: eventId ?? $"evt_{Guid.NewGuid()}",
            SchemaVersion: schemaVersion,
            TenantId: tenantId,
            AggregateType: aggregateType,
            AggregateId: $"dev_{Guid.NewGuid()}",
            AggregateVersion: 1,
            CorrelationId: $"evt_{Guid.NewGuid()}",
            CausationId: null,
            OccurredAt: now,
            ReceivedAt: now,
            Producer: "test-fixture",
            PayloadClassification: classification,
            Payload: payload ?? new Dictionary<string, string>
            {
                ["action"] = "status_changed",
                ["summary"] = "Device SN-TEST-1 status changed to Active.",
                ["reference"] = "SN-TEST-1",
            });
    }
}

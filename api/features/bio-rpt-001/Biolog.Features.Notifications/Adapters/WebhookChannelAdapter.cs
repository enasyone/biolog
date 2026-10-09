namespace Biolog.Features.Notifications.Adapters;

/// <summary>
/// Webhook adapter (BIO-RPT-001-AC02). A machine consumer (AC04): carries
/// <c>summary</c>/<c>action</c>/<c>reference</c> plus non-sensitive envelope metadata
/// (<c>event_id</c>, <c>tenant_id</c>, <c>aggregate_type</c>) so a receiving system can
/// correlate without this project ever forwarding the raw payload object itself - still
/// empty of payload fields for personal/sensitive-classified events
/// (<see cref="PayloadAllowlist"/>).
/// </summary>
public sealed class WebhookChannelAdapter : INotificationChannelAdapter
{
    private static readonly HashSet<string> AllowedKeys = ["summary", "action", "reference"];

    public NotificationChannel Channel => NotificationChannel.Webhook;

    public RenderedNotification Render(DomainEventEnvelope envelope, NotificationRule rule)
    {
        var fields = new Dictionary<string, string>(PayloadAllowlist.Project(envelope, AllowedKeys))
        {
            ["event_id"] = envelope.EventId,
            ["tenant_id"] = envelope.TenantId,
            ["aggregate_type"] = envelope.AggregateType.ToString(),
        };

        var summary = fields.TryGetValue("summary", out var value)
            ? value
            : $"{envelope.AggregateType} event ({rule.Name}) - details withheld.";

        return new RenderedNotification(summary, fields);
    }

    public Task<ChannelDeliveryResult> SendAsync(
        DomainEventEnvelope envelope, RenderedNotification notification, CancellationToken cancellationToken) =>
        Task.FromResult(envelope.Payload.GetValueOrDefault("simulate") switch
        {
            "transient_failure" => new ChannelDeliveryResult(ChannelDeliveryOutcome.TransientFailure, "receiving endpoint returned HTTP 503 (simulated)."),
            "permanent_failure" => new ChannelDeliveryResult(ChannelDeliveryOutcome.PermanentFailure, "receiving endpoint returned HTTP 410 (simulated)."),
            _ => new ChannelDeliveryResult(ChannelDeliveryOutcome.Delivered, "accepted by receiving endpoint (simulated)."),
        });
}

namespace Biolog.Features.Notifications.Adapters;

/// <summary>
/// SMS adapter (BIO-RPT-001-AC02). Tightest per-channel allowlist of the five (AC04): a
/// device-facing text channel carries only an opaque <c>reference</c>, never a
/// human-readable summary or the event's own action name - the narrowest floor any channel
/// in this story gets, matching SMS's own real-world constraints (no rich body, often
/// displayed on a lock screen).
/// </summary>
public sealed class SmsChannelAdapter : INotificationChannelAdapter
{
    private static readonly HashSet<string> AllowedKeys = ["reference"];

    public NotificationChannel Channel => NotificationChannel.Sms;

    public RenderedNotification Render(DomainEventEnvelope envelope, NotificationRule rule)
    {
        var fields = PayloadAllowlist.Project(envelope, AllowedKeys);
        var reference = fields.GetValueOrDefault("reference", envelope.EventId);
        return new RenderedNotification($"BIOLOG alert. Ref {reference}.", fields);
    }

    public Task<ChannelDeliveryResult> SendAsync(
        DomainEventEnvelope envelope, RenderedNotification notification, CancellationToken cancellationToken) =>
        Task.FromResult(envelope.Payload.GetValueOrDefault("simulate") switch
        {
            "transient_failure" => new ChannelDeliveryResult(ChannelDeliveryOutcome.TransientFailure, "SMS gateway busy (simulated)."),
            "permanent_failure" => new ChannelDeliveryResult(ChannelDeliveryOutcome.PermanentFailure, "number is not reachable (simulated)."),
            _ => new ChannelDeliveryResult(ChannelDeliveryOutcome.Delivered, "accepted by SMS gateway (simulated)."),
        });
}

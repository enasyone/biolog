namespace Biolog.Features.Notifications.Adapters;

/// <summary>
/// Push adapter (BIO-RPT-001-AC02). Slightly wider than SMS (AC04): a push notification's
/// title area can carry a short <c>action</c> label in addition to <c>reference</c>, but
/// never the fuller <c>summary</c> email gets - a push payload is still rendered on a lock
/// screen, so it stays short and generic.
/// </summary>
public sealed class PushChannelAdapter : INotificationChannelAdapter
{
    private static readonly HashSet<string> AllowedKeys = ["action", "reference"];

    public NotificationChannel Channel => NotificationChannel.Push;

    public RenderedNotification Render(DomainEventEnvelope envelope, NotificationRule rule)
    {
        var fields = PayloadAllowlist.Project(envelope, AllowedKeys);
        var action = fields.GetValueOrDefault("action", "update");
        return new RenderedNotification($"BIOLOG: {action}", fields);
    }

    public Task<ChannelDeliveryResult> SendAsync(
        DomainEventEnvelope envelope, RenderedNotification notification, CancellationToken cancellationToken) =>
        Task.FromResult(envelope.Payload.GetValueOrDefault("simulate") switch
        {
            "transient_failure" => new ChannelDeliveryResult(ChannelDeliveryOutcome.TransientFailure, "push provider timed out (simulated)."),
            "permanent_failure" => new ChannelDeliveryResult(ChannelDeliveryOutcome.PermanentFailure, "device token no longer registered (simulated)."),
            _ => new ChannelDeliveryResult(ChannelDeliveryOutcome.Delivered, "accepted by push provider (simulated)."),
        });
}

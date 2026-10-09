namespace Biolog.Features.Notifications.Adapters;

/// <summary>
/// Email adapter (BIO-RPT-001-AC02). Widest per-channel allowlist of the five (AC04: email
/// is the one channel this story treats as able to carry a fuller, human-readable summary),
/// still never the raw payload - only the three keys named below, and never at all for
/// <see cref="EventClassification.Personal"/>/<see cref="EventClassification.Sensitive"/>
/// (see <see cref="PayloadAllowlist"/>).
///
/// No real SMTP/vendor dependency exists in this environment - <see cref="SendAsync"/> is a
/// deterministic simulator, driven by an explicit <c>"simulate"</c> payload marker (never
/// itself allowlisted into a rendered message) so tests can exercise every outcome without
/// randomness, the same convention BIO-DEV-003's vendor SDK simulators established.
/// </summary>
public sealed class EmailChannelAdapter : INotificationChannelAdapter
{
    private static readonly HashSet<string> AllowedKeys = ["summary", "action", "reference"];

    public NotificationChannel Channel => NotificationChannel.Email;

    public RenderedNotification Render(DomainEventEnvelope envelope, NotificationRule rule)
    {
        var fields = PayloadAllowlist.Project(envelope, AllowedKeys);
        var summary = fields.TryGetValue("summary", out var value)
            ? value
            : $"{envelope.AggregateType} event for {rule.Name} (details withheld).";

        return new RenderedNotification(summary, fields);
    }

    public Task<ChannelDeliveryResult> SendAsync(
        DomainEventEnvelope envelope, RenderedNotification notification, CancellationToken cancellationToken) =>
        Task.FromResult(envelope.Payload.GetValueOrDefault("simulate") switch
        {
            "transient_failure" => new ChannelDeliveryResult(ChannelDeliveryOutcome.TransientFailure, "SMTP relay timed out (simulated)."),
            "permanent_failure" => new ChannelDeliveryResult(ChannelDeliveryOutcome.PermanentFailure, "recipient mailbox rejected (simulated)."),
            _ => new ChannelDeliveryResult(ChannelDeliveryOutcome.Delivered, "accepted by mail relay (simulated)."),
        });
}

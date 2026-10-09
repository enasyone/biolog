namespace Biolog.Features.Notifications.Adapters;

/// <summary>
/// Teams adapter (BIO-RPT-001-AC02). A structured, operator-facing channel (AC04): carries
/// <c>summary</c>/<c>action</c>/<c>reference</c> plus the event's own aggregate type as
/// context - still never the raw payload, and still empty for
/// personal/sensitive-classified events like every other channel (<see cref="PayloadAllowlist"/>).
/// </summary>
public sealed class TeamsChannelAdapter : INotificationChannelAdapter
{
    private static readonly HashSet<string> AllowedKeys = ["summary", "action", "reference"];

    public NotificationChannel Channel => NotificationChannel.Teams;

    public RenderedNotification Render(DomainEventEnvelope envelope, NotificationRule rule)
    {
        var fields = new Dictionary<string, string>(PayloadAllowlist.Project(envelope, AllowedKeys))
        {
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
            "transient_failure" => new ChannelDeliveryResult(ChannelDeliveryOutcome.TransientFailure, "Teams webhook rate-limited (simulated)."),
            "permanent_failure" => new ChannelDeliveryResult(ChannelDeliveryOutcome.PermanentFailure, "Teams channel webhook was removed (simulated)."),
            _ => new ChannelDeliveryResult(ChannelDeliveryOutcome.Delivered, "accepted by Teams incoming webhook (simulated)."),
        });
}

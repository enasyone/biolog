namespace Biolog.Features.Notifications;

/// <summary>A rendered, channel-specific message - already minimized (BIO-RPT-001-AC04)
/// before it ever reaches <see cref="INotificationChannelAdapter.SendAsync"/>, so a
/// transport can never accidentally forward a field it was never given.</summary>
public sealed record RenderedNotification(string Summary, IReadOnlyDictionary<string, string> Fields);

public enum ChannelDeliveryOutcome
{
    Delivered,
    TransientFailure,
    PermanentFailure,
}

public sealed record ChannelDeliveryResult(ChannelDeliveryOutcome Outcome, string Detail);

/// <summary>
/// BIO-RPT-001-AC02: "Channel adapters are separated." One implementation per
/// <see cref="NotificationChannel"/> (<c>Adapters/*.cs</c>), each responsible for its own
/// <see cref="Render"/> (AC04's per-channel minimization) and its own simulated transport
/// (<see cref="SendAsync"/> - no real email/SMS/push/Teams/webhook vendor exists to
/// integrate with in this environment, so every adapter is a deterministic simulator, the
/// same "framework real, transport simulated" shape BIO-DEV-003's vendor SDK simulators
/// already established for a different kind of external dependency).
///
/// <see cref="NotificationDispatchService"/> never branches on
/// <see cref="NotificationChannel"/> itself - it looks up the one adapter whose
/// <see cref="Channel"/> matches a rule's and calls only these two methods, so adding a
/// "future channel" (the user story's own words) means adding one new adapter class and one
/// new enum value, never touching the dispatcher.
/// </summary>
public interface INotificationChannelAdapter
{
    NotificationChannel Channel { get; }

    RenderedNotification Render(DomainEventEnvelope envelope, NotificationRule rule);

    /// <summary><paramref name="envelope"/> is passed alongside the already-rendered
    /// <paramref name="notification"/> only so a simulator can read the test-only
    /// <c>"simulate"</c> payload marker (never itself allowlisted into
    /// <see cref="RenderedNotification.Fields"/> - see each adapter's own <c>Render</c>) to
    /// pick a deterministic outcome; a real transport implementation would ignore
    /// <paramref name="envelope"/> entirely and send only what <paramref name="notification"/>
    /// carries.</summary>
    Task<ChannelDeliveryResult> SendAsync(DomainEventEnvelope envelope, RenderedNotification notification, CancellationToken cancellationToken);
}

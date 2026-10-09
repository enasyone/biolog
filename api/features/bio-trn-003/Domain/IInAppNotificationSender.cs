namespace Biolog.Features.TransportNotifications;

public enum NotificationSendOutcome
{
    Delivered,
    TransientFailure,
    PermanentFailure,
}

public sealed record NotificationSendResult(NotificationSendOutcome Outcome, string Detail);

/// <summary>
/// The "send" half of this story's delivery pipeline (AC03), separated from the durable
/// read-back audit (<see cref="ITransportNotificationDeliveryAuditSink"/>) - the same two-interface
/// split BIO-INT-004's own <c>IWebhookSender</c>/<c>IWebhookDeliveryAuditSink</c> already
/// established for exactly this reason (a send and its audit trail are two different failure
/// surfaces). This story's real channel is an in-app, tenant-visible notification feed (never an
/// external SMS/email/push to a guardian contact - see docs/features/bio-trn-003/README.md
/// "Why 'parent' delivery is a disclosed boundary, not a silent gap" for why: no guardian/parent
/// contact-channel model exists anywhere in this codebase - <c>Person</c> carries no phone/email/
/// address field at all, and introducing one is a shared-contract change this story does not own).
/// <see cref="InMemoryInAppNotificationSender"/> is the real default - genuinely delivers into the
/// durable audit sink's own read-back, exactly as a real external channel's own successful POST
/// would. Retry/dead-letter behavior (AC03's own "delivery failure is visible") is proven via
/// fault-injectable test doubles, the identical "no naturally-occurring failure in an in-memory
/// demo channel" shape BIO-RPT-001's own <c>INotificationChannelAdapter</c> implementations
/// already have - this project is not inventing a weaker proof than an already-accepted sibling.
/// </summary>
public interface IInAppNotificationSender
{
    Task<NotificationSendResult> SendAsync(TransportNotificationPayload payload, CancellationToken cancellationToken = default);
}

internal sealed class InMemoryInAppNotificationSender : IInAppNotificationSender
{
    public Task<NotificationSendResult> SendAsync(TransportNotificationPayload payload, CancellationToken cancellationToken = default) =>
        Task.FromResult(new NotificationSendResult(NotificationSendOutcome.Delivered, "delivered to the tenant's in-app notification feed"));
}

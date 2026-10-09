namespace Biolog.Features.TransportEvents.Domain;

/// <summary>Append-only store for <see cref="TriggeredNotification"/> - AC04's own read-back seam so a caller (and a test) can prove a configured condition actually fired for a real event.</summary>
public interface ITriggeredNotificationLog
{
    Task AppendAsync(TriggeredNotification notification, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TriggeredNotification>> ListForTenantAsync(string tenantId, CancellationToken cancellationToken = default);
}

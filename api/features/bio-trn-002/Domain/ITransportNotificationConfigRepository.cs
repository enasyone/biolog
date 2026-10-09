namespace Biolog.Features.TransportEvents.Domain;

public interface ITransportNotificationConfigRepository
{
    /// <summary>Never null - a tenant with no explicit config yet still has an effective one, <see cref="TransportNotificationConfig.Default"/>.</summary>
    Task<TransportNotificationConfig> GetAsync(string tenantId, CancellationToken cancellationToken = default);

    /// <summary>Compare-and-set on <see cref="TransportNotificationConfig.Version"/>. Returns false on a version mismatch rather than throwing.</summary>
    Task<bool> TrySaveAsync(TransportNotificationConfig config, int expectedVersion, CancellationToken cancellationToken = default);
}

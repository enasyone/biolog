using System.Collections.Concurrent;

namespace Biolog.Features.TransportEvents.Domain;

/// <summary>Process-lifetime, volatile - documentation precision, not a durability claim.</summary>
public sealed class InMemoryTriggeredNotificationLog : ITriggeredNotificationLog
{
    private readonly ConcurrentBag<TriggeredNotification> _entries = [];

    public Task AppendAsync(TriggeredNotification notification, CancellationToken cancellationToken = default)
    {
        _entries.Add(notification);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<TriggeredNotification>> ListForTenantAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        var results = _entries.Where(n => n.TenantId == tenantId).OrderBy(n => n.TriggeredAt, StringComparer.Ordinal).ToList();
        return Task.FromResult<IReadOnlyList<TriggeredNotification>>(results);
    }
}

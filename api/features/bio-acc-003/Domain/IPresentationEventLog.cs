using System.Collections.Concurrent;

namespace Biolog.Features.AccessCache.Domain;

/// <summary>
/// Durable, tenant-scoped store for uploaded <see cref="PresentationEvent"/>s - the source this
/// feature's human-facing "view access events" read serves from (AC06's <c>AccessService</c>
/// wiring). Append-only by construction: no method here can modify or remove an already-appended
/// event, the same "no orphaned/silently-edited audit-adjacent history" discipline
/// <c>IPersonRepository</c>'s own missing-delete-method establishes for a different aggregate.
/// </summary>
public interface IPresentationEventLog
{
    Task AppendAsync(PresentationEvent presentationEvent, CancellationToken cancellationToken = default);

    /// <summary>Most recent first (by <see cref="PresentationEvent.OccurredAt"/>), capped at <paramref name="limit"/>.</summary>
    Task<IReadOnlyList<PresentationEvent>> ListRecentAsync(string tenantId, int limit, CancellationToken cancellationToken = default);
}

internal sealed class InMemoryPresentationEventLog : IPresentationEventLog
{
    private readonly ConcurrentDictionary<string, PresentationEvent> _events = new(StringComparer.Ordinal);

    public Task AppendAsync(PresentationEvent presentationEvent, CancellationToken cancellationToken = default)
    {
        _events[presentationEvent.EventId] = presentationEvent;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<PresentationEvent>> ListRecentAsync(string tenantId, int limit, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<PresentationEvent> items = [.. _events.Values
            .Where(e => e.TenantId == tenantId)
            .OrderByDescending(e => e.OccurredAt, StringComparer.Ordinal)
            .ThenByDescending(e => e.EventId, StringComparer.Ordinal)
            .Take(Math.Max(0, limit))];
        return Task.FromResult(items);
    }
}

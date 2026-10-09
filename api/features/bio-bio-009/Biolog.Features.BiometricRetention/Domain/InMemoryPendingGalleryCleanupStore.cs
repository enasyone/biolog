using System.Collections.Concurrent;

namespace Biolog.Features.BiometricRetention.Domain;

internal sealed class InMemoryPendingGalleryCleanupStore : IPendingGalleryCleanupStore
{
    private readonly ConcurrentDictionary<(string TenantId, BiometricRecordKind Kind, string PersonId), PendingGalleryCleanup> _pending = new();

    public Task RecordAsync(PendingGalleryCleanup entry, CancellationToken cancellationToken)
    {
        _pending[(entry.TenantId, entry.Kind, entry.PersonId)] = entry;
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string tenantId, BiometricRecordKind kind, string personId, CancellationToken cancellationToken)
    {
        _pending.TryRemove((tenantId, kind, personId), out _);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<PendingGalleryCleanup>> ListAsync(string tenantId, CancellationToken cancellationToken)
    {
        IReadOnlyList<PendingGalleryCleanup> result = _pending.Values
            .Where(entry => entry.TenantId == tenantId)
            .ToList();
        return Task.FromResult(result);
    }
}

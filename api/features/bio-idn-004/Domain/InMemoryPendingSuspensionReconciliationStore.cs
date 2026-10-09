using System.Collections.Concurrent;

namespace Biolog.Features.PersonLifecycle.Domain;

/// <summary>In-process store for BIO-IDN-004's pending suspension-revocation reconciliations. Process-lifetime only, not durable across a restart. See <see cref="IPendingSuspensionReconciliationStore"/>.</summary>
internal sealed class InMemoryPendingSuspensionReconciliationStore : IPendingSuspensionReconciliationStore
{
    private readonly ConcurrentDictionary<(string TenantId, string PersonId, int SuspendedAtVersion), PendingSuspensionReconciliation> _pending = new();

    public Task MarkPendingAsync(PendingSuspensionReconciliation pending, CancellationToken cancellationToken = default)
    {
        _pending[(pending.TenantId, pending.PersonId, pending.SuspendedAtVersion)] = pending;
        return Task.CompletedTask;
    }

    public Task ClearAsync(string tenantId, string personId, int suspendedAtVersion, CancellationToken cancellationToken = default)
    {
        _pending.TryRemove((tenantId, personId, suspendedAtVersion), out _);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<PendingSuspensionReconciliation>> ListPendingAsync(string tenantId, string personId, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<PendingSuspensionReconciliation> items =
        [
            .. _pending.Values
                .Where(p => p.TenantId == tenantId && p.PersonId == personId)
                .OrderBy(p => p.SuspendedAtVersion),
        ];
        return Task.FromResult(items);
    }
}

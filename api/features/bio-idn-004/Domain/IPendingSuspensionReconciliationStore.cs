namespace Biolog.Features.PersonLifecycle.Domain;

/// <summary>
/// Durable store for <see cref="PendingSuspensionReconciliation"/> records - see that type's
/// header for why this cannot be a status-derived computation. Keyed by
/// <c>(TenantId, PersonId, SuspendedAtVersion)</c>, matching
/// <see cref="ICredentialAccessRevocationEventSink.ExistsForSuspensionAsync"/>'s own key shape.
/// </summary>
internal interface IPendingSuspensionReconciliationStore
{
    Task MarkPendingAsync(PendingSuspensionReconciliation pending, CancellationToken cancellationToken = default);

    Task ClearAsync(string tenantId, string personId, int suspendedAtVersion, CancellationToken cancellationToken = default);

    /// <summary>Every suspension for this person still awaiting a confirmed revocation event, oldest first - regardless of the person's CURRENT status.</summary>
    Task<IReadOnlyList<PendingSuspensionReconciliation>> ListPendingAsync(string tenantId, string personId, CancellationToken cancellationToken = default);
}

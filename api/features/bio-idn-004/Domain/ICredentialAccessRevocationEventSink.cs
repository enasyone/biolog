namespace Biolog.Features.PersonLifecycle.Domain;

/// <summary>
/// Append-only store for <see cref="CredentialAccessRevocationEvent"/>s - BIO-IDN-004-AC02's
/// real emission surface. See that type's header for why this is a separate stream from
/// <see cref="IPersonLifecycleAuditSink"/> and why no live consumer is wired to it yet.
/// </summary>
public interface ICredentialAccessRevocationEventSink
{
    Task PublishAsync(CredentialAccessRevocationEvent revocationEvent, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default);

    /// <summary>
    /// BIO-IDN-004-QA-01 (Seyi round 2): whether a revocation event for THIS SPECIFIC suspension
    /// (identified by <see cref="CredentialAccessRevocationEvent.SuspendedAtVersion"/>) has
    /// already been recorded - the query <see cref="PersonLifecycleService"/> uses to make
    /// reconciliation idempotent (never double-publish, always safe to re-ask).
    /// </summary>
    Task<bool> ExistsForSuspensionAsync(string tenantId, string personId, int suspendedAtVersion, CancellationToken cancellationToken = default);
}

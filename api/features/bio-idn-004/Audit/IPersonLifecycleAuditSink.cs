namespace Biolog.Features.PersonLifecycle.Audit;

/// <summary>
/// Append-only store for <see cref="PersonLifecycleAuditEvent"/>s. <see cref="WriteAsync"/>
/// throwing does not, on its own, prove the event was never persisted - <see cref="ExistsAsync"/>
/// exists so a caller can resolve that ambiguity by checking ground truth instead of guessing
/// (ADR-0003 Decision 7, element 4) - see <c>PersonLifecycleService</c>'s header "Audit
/// ordering" for why this sink's own confirmation, not the gateway call, is what this project's
/// audit-atomicity discipline actually protects.
/// </summary>
public interface IPersonLifecycleAuditSink
{
    Task WriteAsync(PersonLifecycleAuditEvent auditEvent, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default);

    /// <summary>BIO-IDN-004-AC04: every lifecycle event ever recorded for one person, oldest first - proof that archived records remain traceable.</summary>
    Task<IReadOnlyList<PersonLifecycleAuditEvent>> ListByPersonAsync(string tenantId, string personId, CancellationToken cancellationToken = default);
}

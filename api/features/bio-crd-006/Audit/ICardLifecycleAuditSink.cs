namespace Biolog.Features.CardLifecycle.Audit;

/// <summary>
/// Append-only store for <see cref="CardLifecycleAuditEvent"/>s. Mandatory, not optional
/// (ADR-0002 §5 / ADR-0003 Decision 7): <see cref="CardLifecycle.CardLifecycleService"/> awaits
/// this before treating a mutation as complete, and a failure here surfaces to the caller as
/// <c>audit_unavailable</c> rather than a silent, unaudited success - report/recover/
/// activate-replacement are exactly the kind of security-sensitive actions ADR-0003 requires an
/// audit trail for, and BIO-CRD-005-QA-01 (this codebase's own prior incident) is the concrete
/// reason every mutation here audits strictly BEFORE committing, never after.
///
/// <see cref="WriteAsync"/> throwing does not, on its own, prove the event was never
/// persisted; <see cref="ExistsAsync"/> resolves that ambiguity (ADR-0003 Decision 7, element
/// 4) - see <see cref="CardLifecycle.CardLifecycleService.AuditAsync"/>.
/// </summary>
public interface ICardLifecycleAuditSink
{
    Task WriteAsync(CardLifecycleAuditEvent auditEvent, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default);
}

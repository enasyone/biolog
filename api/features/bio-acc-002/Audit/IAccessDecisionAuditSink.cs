namespace Biolog.Features.AccessDecisions.Audit;

/// <summary>
/// Append-only store for <see cref="AccessDecisionAuditEvent"/>s. Mandatory, not optional
/// (ADR-0002 S5 / ADR-0003 Decision 7): <see cref="AccessDecisionService"/> awaits this before
/// treating a decision as committed, and a failure here surfaces to the caller as
/// <c>audit_unavailable</c> rather than a silent, unaudited access decision.
///
/// <see cref="WriteAsync"/> throwing does not, on its own, prove the event was never persisted -
/// <see cref="ExistsAsync"/> exists so a caller can resolve that ambiguity by checking ground
/// truth instead of guessing (ADR-0003 Decision 7, element 4).
/// </summary>
public interface IAccessDecisionAuditSink
{
    Task WriteAsync(AccessDecisionAuditEvent auditEvent, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default);
}

namespace Biolog.Features.CardIssuance.Audit;

/// <summary>
/// Append-only store for <see cref="CardIssueAuditEvent"/>s. Mandatory, not optional
/// (ADR-0002 §5 / ADR-0003 Decision 7): <see cref="CardIssuance.CardIssueService"/> awaits
/// this before treating a mutation as complete, and a failure here surfaces to the caller as
/// <c>audit_unavailable</c> rather than a silent, unaudited success - every mutation on a
/// physical card (issue/encode/print/verify/activate/deactivate) is exactly the kind of
/// security/operations-sensitive action ADR-0003 requires an audit trail for.
///
/// <see cref="WriteAsync"/> throwing does not, on its own, prove the event was never
/// persisted; <see cref="ExistsAsync"/> resolves that ambiguity (ADR-0003 Decision 7, element
/// 4) - see <see cref="CardIssuance.CardIssueService.AuditAsync"/>.
/// </summary>
public interface ICardIssueAuditSink
{
    Task WriteAsync(CardIssueAuditEvent auditEvent, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default);
}

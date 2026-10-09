namespace Biolog.Features.InstitutionalIdentifiers.Audit;

/// <summary>
/// Append-only store for <see cref="InstitutionalIdentifierAuditEvent"/>s. Mandatory, not
/// optional (ADR-0002 §5 / ADR-0003 Decision 7): both
/// <see cref="InstitutionalIdentifierService"/> and <see cref="IdentifierUniquenessRuleService"/>
/// await this before treating a mutation as complete, and a failure here surfaces to the caller
/// as <c>audit_unavailable</c> rather than a silent, unaudited success.
///
/// <see cref="WriteAsync"/> throwing does not, on its own, prove the event was never
/// persisted. <see cref="ExistsAsync"/> exists so a caller can resolve that ambiguity by
/// checking ground truth instead of guessing (ADR-0003 Decision 7, element 4).
/// </summary>
public interface IInstitutionalIdentifierAuditSink
{
    Task WriteAsync(InstitutionalIdentifierAuditEvent auditEvent, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default);
}

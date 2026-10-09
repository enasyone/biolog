namespace Biolog.Features.AuditInvestigation.Audit;

/// <summary>
/// Append-only store for this feature's OWN <see cref="AuditInvestigationAuditEvent"/>s (mandatory,
/// not optional - ADR-0002 §5 / ADR-0003 Decision 7's ordering rule: "that ordering applies to
/// every audited effect, not only denials"). An export's CSV bytes are never returned to the caller
/// until this sink's write has settled - see <see cref="Biolog.Features.AuditInvestigation.AuditInvestigationService"/>.
///
/// <see cref="WriteAsync"/> throwing does not, on its own, prove the event was never persisted;
/// <see cref="ExistsAsync"/> resolves that ambiguity (ADR-0003 Decision 7, element 4). Export
/// mutates no aggregate - there is no per-aggregate lock/CAS to hold (Decision 7 elements 1-3 do
/// not apply to a pure read+export), only the audit-before-effect ORDERING itself, which is what
/// this sink's contract exists to make possible.
/// </summary>
public interface IAuditInvestigationAuditSink
{
    Task WriteAsync(AuditInvestigationAuditEvent auditEvent, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default);
}

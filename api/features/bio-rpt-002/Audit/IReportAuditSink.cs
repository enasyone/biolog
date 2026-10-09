namespace Biolog.Features.ReportBuilder.Audit;

/// <summary>
/// Append-only store for <see cref="ReportAuditEvent"/>s. Mandatory (ADR-0002 §5 / ADR-0003
/// Decision 7): every report generation/export attempt audits BEFORE the caller sees a result -
/// AC05's own requirement IS this audit trail.
///
/// <see cref="WriteAsync"/> throwing does not, on its own, prove the event was never persisted;
/// <see cref="ExistsAsync"/> resolves that ambiguity (ADR-0003 Decision 7, element 4).
/// </summary>
public interface IReportAuditSink
{
    Task WriteAsync(ReportAuditEvent auditEvent, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default);
}

namespace Biolog.Features.CardProduction.Audit;

/// <summary>
/// Append-only store for <see cref="ProductionAuditEvent"/>s. Mandatory (ADR-0002 §5 / ADR-0003
/// Decision 7): every mutation on a production job/package audits BEFORE committing, and a
/// failure here surfaces as <c>audit_unavailable</c> rather than a silent, unaudited success -
/// AC04's own "production report" IS this audit trail plus the job record itself.
///
/// <see cref="WriteAsync"/> throwing does not, on its own, prove the event was never persisted;
/// <see cref="ExistsAsync"/> resolves that ambiguity (ADR-0003 Decision 7, element 4).
/// </summary>
public interface IProductionAuditSink
{
    Task WriteAsync(ProductionAuditEvent auditEvent, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default);
}

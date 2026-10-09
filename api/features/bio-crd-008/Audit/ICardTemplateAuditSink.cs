namespace Biolog.Features.CardTemplates.Audit;

/// <summary>
/// Append-only store for <see cref="CardTemplateAuditEvent"/>s (provenance - every draft/edit/
/// submit/validate/reject/publish/retire transition audits BEFORE the caller sees a result).
///
/// <see cref="WriteAsync"/> throwing does not, on its own, prove the event was never persisted;
/// <see cref="ExistsAsync"/> resolves that ambiguity (ADR-0003 Decision 7, element 4).
/// </summary>
public interface ICardTemplateAuditSink
{
    Task WriteAsync(CardTemplateAuditEvent auditEvent, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default);
}

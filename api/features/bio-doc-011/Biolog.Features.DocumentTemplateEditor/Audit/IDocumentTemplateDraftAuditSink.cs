namespace Biolog.Features.DocumentTemplateEditor.Audit;

/// <summary>
/// Append-only store for <see cref="DocumentTemplateDraftAuditEvent"/>s (provenance - every
/// create/attach-layout/define-regions/submit/record-corpus-run/publish/create-next-version
/// transition audits BEFORE the caller sees a result).
///
/// <see cref="WriteAsync"/> throwing does not, on its own, prove the event was never persisted;
/// <see cref="ExistsAsync"/> resolves that ambiguity (ADR-0003 Decision 7, element 4).
/// </summary>
public interface IDocumentTemplateDraftAuditSink
{
    Task WriteAsync(DocumentTemplateDraftAuditEvent auditEvent, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default);
}

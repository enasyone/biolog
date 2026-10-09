namespace Biolog.Features.DocumentTemplatePack.Audit;

/// <summary>
/// Durable append-only store for <see cref="DocumentTemplatePackAuditEvent"/>s. Mandatory, not
/// optional (ADR-0002 S5's "the audit sink is a hard dependency"): both
/// <see cref="Model.DocumentTemplatePack.Publish"/> and
/// <see cref="Model.DocumentTemplateVerification.Verify"/> write through this before returning,
/// the same audit-before-commit pattern every hard-audited BIOLOG feature in this epic uses.
///
/// <see cref="ExistsAsync"/> resolves the ambiguous case <see cref="WriteAsync"/> throwing does
/// not, on its own, prove nothing was persisted.
/// </summary>
public interface IDocumentTemplatePackAuditSink
{
    Task WriteAsync(DocumentTemplatePackAuditEvent auditEvent, CancellationToken cancellationToken = default);

    /// <summary>True if an event with this exact <c>event_id</c> is durably present, false if it
    /// definitely is not. Called only to resolve a <see cref="WriteAsync"/> that threw.</summary>
    Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default);
}

/// <summary>Thrown when the audit sink is genuinely unavailable (a write threw and could not be
/// confirmed durable, or the existence check itself failed) - a publish/verification is never
/// returned as successful when this is thrown.</summary>
public sealed class DocumentTemplatePackAuditUnavailableException(string message, Exception? innerException = null)
    : InvalidOperationException(message, innerException);

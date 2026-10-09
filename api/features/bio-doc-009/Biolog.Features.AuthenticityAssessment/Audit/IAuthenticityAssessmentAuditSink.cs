namespace Biolog.Features.AuthenticityAssessment.Audit;

/// <summary>
/// Durable append-only store for <see cref="AuthenticityAssessmentAuditEvent"/>s. Mandatory, not
/// optional (ADR-0002 S5's "the audit sink is a hard dependency"): both
/// <see cref="Model.AuthenticityAssessmentResult.Compute"/> and
/// <see cref="Model.AuthenticityAssessment.ApplyManualOverride"/> write through this before
/// returning, using the same audit-before-commit pattern BIO-DOC-008's own
/// <c>CrossComparisonResult.Compute</c> established for this epic.
///
/// <see cref="ExistsAsync"/> resolves the ambiguous case <see cref="WriteAsync"/> throwing does
/// not, on its own, prove nothing was persisted - the same ambiguous-write reasoning
/// <c>IEvidenceComparisonAuditSink</c>/<c>ITenantAuditSink</c> already established for this
/// codebase's audit policy.
/// </summary>
public interface IAuthenticityAssessmentAuditSink
{
    Task WriteAsync(AuthenticityAssessmentAuditEvent auditEvent, CancellationToken cancellationToken = default);

    /// <summary>True if an event with this exact <c>event_id</c> is durably present, false if it
    /// definitely is not. Called only to resolve a <see cref="WriteAsync"/> that threw.</summary>
    Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default);
}

/// <summary>Thrown when the audit sink is genuinely unavailable (a write threw and could not be
/// confirmed durable, or the existence check itself failed) - an assessment/override is never
/// returned as successful when this is thrown.</summary>
public sealed class AuthenticityAssessmentAuditUnavailableException(string message, Exception? innerException = null)
    : InvalidOperationException(message, innerException);

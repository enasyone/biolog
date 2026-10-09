namespace Biolog.Features.EvidenceComparison.Audit;

/// <summary>
/// Durable append-only store for <see cref="EvidenceComparisonAuditEvent"/>s. Mandatory, not
/// optional (ADR-0002 §5's "the audit sink is a hard dependency"): <see cref="CrossComparisonResult.Compute"/>
/// awaits this before returning a comparison result as successful, and a failure here surfaces as
/// a thrown <see cref="EvidenceComparisonAuditUnavailableException"/> rather than a silent,
/// unaudited "successful" comparison (BIO-DOC-008-QA-01, Chioma's acceptance review).
///
/// <see cref="ExistsAsync"/> resolves the ambiguous case <see cref="WriteAsync"/> throwing does
/// not, on its own, prove nothing was persisted - a real store can durably commit a write and
/// still throw on the acknowledgement (a timeout, a dropped connection after commit) - the same
/// ambiguous-write reasoning <c>ITenantAuditSink</c> (BIO-TEN-001-QA-01 round 4) already
/// established for this codebase's audit policy.
/// </summary>
public interface IEvidenceComparisonAuditSink
{
    Task WriteAsync(EvidenceComparisonAuditEvent auditEvent, CancellationToken cancellationToken = default);

    /// <summary>True if an event with this exact <c>event_id</c> is durably present, false if it
    /// definitely is not. Called only to resolve a <see cref="WriteAsync"/> that threw.</summary>
    Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default);
}

/// <summary>Thrown when the audit sink is genuinely unavailable (a write threw and could not be
/// confirmed durable, or the existence check itself failed) - a comparison is never returned as
/// successful when this is thrown.</summary>
public sealed class EvidenceComparisonAuditUnavailableException(string message, Exception? innerException = null)
    : InvalidOperationException(message, innerException);

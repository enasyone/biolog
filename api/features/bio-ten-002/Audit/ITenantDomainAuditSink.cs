namespace Biolog.Features.TenantDomainRegistry.Audit;

/// <summary>
/// Append-only store for <see cref="TenantDomainAuditEvent"/>s. Mandatory, not optional
/// (ADR-0002 §5 / ADR-0003 Decision 7): <see cref="TenantDomainService"/> awaits this before
/// treating an administrative change as complete, and a failure here surfaces to the caller
/// as <c>audit_unavailable</c> rather than a silent, unaudited success.
///
/// <see cref="WriteAsync"/> throwing does not, on its own, prove the event was never
/// persisted - a real store can commit a write and still throw on the acknowledgement (a
/// timeout, a dropped connection after commit). <see cref="ExistsAsync"/> exists so a caller
/// can resolve that ambiguity by checking ground truth instead of guessing - see
/// <c>TenantDomainService.AuditAsync</c> for how it is used (ADR-0003 Decision 7, element 4).
///
/// <b>Documentation precision, not a claim:</b> this interface does not describe its
/// implementations as "durable" - <see cref="InMemoryTenantDomainAuditSink"/>, the only
/// implementation this story ships, is process-lifetime and volatile, and calling it durable
/// in this doc comment would assert a guarantee that tier cannot give (ADR-0003 Decision 7's
/// "documentation precision, not a defect" callout, itself raised against the identically-
/// shaped BIO-TEN-001 interface). A later story that backs this with a real store is the seam
/// where "durable" becomes an accurate word to use here.
/// </summary>
public interface ITenantDomainAuditSink
{
    Task WriteAsync(TenantDomainAuditEvent auditEvent, CancellationToken cancellationToken = default);

    /// <summary>
    /// True if an event with this exact <c>event_id</c> is present, false if it definitely is
    /// not. Called only to resolve a <see cref="WriteAsync"/> that threw - if THIS also
    /// throws, that is a genuine, total outage of the audit store (not merely one ambiguous
    /// write), the same fail-closed 503 boundary ADR-0002 §5 already accepts for a missing or
    /// unreachable sink.
    /// </summary>
    Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default);
}

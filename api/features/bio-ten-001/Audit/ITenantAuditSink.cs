namespace Biolog.Features.TenantRegistry.Audit;

/// <summary>
/// Durable append-only store for <see cref="TenantAuditEvent"/>s. Mandatory, not optional
/// (ADR-0002 §5's "the audit sink is a hard dependency"): <see cref="TenantService"/> awaits
/// this before treating an administrative change as complete, and a failure here surfaces to
/// the caller as <c>audit_unavailable</c> rather than a silent, unaudited success.
///
/// BIO-TEN-001-QA-01 round 4 (Seyi review): <see cref="WriteAsync"/> throwing does not, on
/// its own, prove the event was never durably persisted - a real store can durably commit a
/// write and still throw on the acknowledgement (a timeout, a dropped connection after
/// commit). A caller that assumes "threw ⟹ nothing happened" can end up in exactly the
/// mismatch that round rejected: an event durably exists claiming success while the caller,
/// believing it failed, never commits the corresponding change. <see cref="ExistsAsync"/>
/// exists so a caller can resolve that ambiguity by checking ground truth instead of
/// guessing - see <c>TenantService.AuditAsync</c> for how it is used. This mirrors ADR-0002
/// §5's own framing exactly: "'Written' means the sink's promise has settled, not that the
/// call was made" - a caller is not entitled to assume settlement from an exception alone.
///
/// <see cref="InMemoryTenantAuditSink"/> backs this for BIO-TEN-001; a real durable sink is
/// the same later-story seam as <see cref="Domain.ITenantRegistry"/>.
/// </summary>
public interface ITenantAuditSink
{
    Task WriteAsync(TenantAuditEvent auditEvent, CancellationToken cancellationToken = default);

    /// <summary>
    /// True if an event with this exact <c>event_id</c> is durably present, false if it
    /// definitely is not. Called only to resolve a <see cref="WriteAsync"/> that threw - if
    /// THIS also throws, that is a genuine, total outage of the audit store (not merely one
    /// ambiguous write), the same fail-closed 503 boundary ADR-0002 §5 already accepts for a
    /// missing or unreachable sink.
    /// </summary>
    Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default);
}

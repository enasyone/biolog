namespace Biolog.Api.Audit;

/// <summary>
/// Where an <see cref="AuditEvent"/> is recorded. ADR-0002 Decision 5 / ADR-0003 Decision 1:
/// "the audit write is a hard dependency of the audited effect, not a side effect a caller or
/// a later refactor can skip" - a caller awaits <see cref="RecordAsync"/> and must treat a
/// thrown exception as the effect itself failing, never as something to log and continue
/// past. There is deliberately no synchronous, fire-and-forget overload. This interface makes
/// no durability promise of its own - that is entirely a property of whatever implementation
/// backs it (<see cref="InMemoryAuditSink"/> is explicitly process-lifetime only, not durable
/// across a restart); callers rely only on the ordering and hard-dependency guarantees above.
/// </summary>
public interface IAuditSink
{
    Task RecordAsync(AuditEvent auditEvent, CancellationToken cancellationToken);

    /// <summary>
    /// True if an event with this exact <c>event_id</c> is present, false if it definitely is
    /// not. ADR-0003 Decision 7 (element 4): called only to resolve a <see cref="RecordAsync"/>
    /// that threw - a real store can durably commit a write and still throw on the
    /// acknowledgement (a timeout, a dropped connection after commit), so "threw" does not by
    /// itself prove "never happened." If THIS also throws, that is a genuine, total outage of
    /// the audit store, not merely one ambiguous write - the same fail-closed boundary ADR-0002
    /// Decision 5 already accepts for a missing or unreachable sink. See
    /// <c>DeviceService.AuditAsync</c> for how it is used, and <c>TenantService.AuditAsync</c>
    /// (BIO-TEN-001) for the reference implementation this mirrors.
    /// </summary>
    Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken);
}

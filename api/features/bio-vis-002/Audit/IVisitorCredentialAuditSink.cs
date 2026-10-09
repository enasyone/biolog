namespace Biolog.Features.VisitorCredentials.Audit;

/// <summary>ADR-0003 Decision 7's audit-atomicity seam: <see cref="ExistsAsync"/> is what lets a service resolve an ambiguous write-throw instead of assuming failure.</summary>
public interface IVisitorCredentialAuditSink
{
    Task WriteAsync(VisitorCredentialAuditEvent auditEvent, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default);

    /// <summary>
    /// BIO-VIS-002-F08's own remediation (Seyi round-8 FAIL / Zainab round-6 FAIL, both
    /// independently): atomically inserts <paramref name="auditEvent"/> only if no event with its
    /// own <c>EventId</c> exists yet, and returns whichever event is now durably stored under that
    /// id - its own payload if this call won the race, or the FIRST writer's payload if a
    /// concurrent caller already won. An append-only audit record must never be silently
    /// overwritten by a second writer racing on the same stable id - see
    /// <c>VisitorCredentialService.ReconcileStrandedCardsAsync</c>'s own header for why
    /// reconciliation (triggered from several call paths that do not all share one lock) needs
    /// this instead of the plain check-then-<see cref="WriteAsync"/> every other call site still
    /// uses with a fresh, collision-free id.
    /// </summary>
    Task<VisitorCredentialAuditEvent> CreateIfAbsentAsync(VisitorCredentialAuditEvent auditEvent, CancellationToken cancellationToken = default);
}

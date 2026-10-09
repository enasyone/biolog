namespace Biolog.Features.EvidenceComparison.Model;

/// <summary>
/// Session-scoped trusted state controlling comparison version sequencing (BIO-DOC-008-QA-03,
/// Chioma's third acceptance review). A bare <c>versionNumber &gt;= 1</c> range check (the
/// original AC05 remediation) still lets a caller label ANY result any positive integer - reuse
/// version 1 for two different comparisons of the same session, skip ahead to 999, or move
/// backwards - none of which a range check alone can catch, since "is this a valid-shaped
/// integer" and "is this the CORRECT next version for this session" are different questions.
///
/// <para>
/// <b>BIO-DOC-008-QA-04 (Chioma's fourth acceptance review):</b> a single-phase "allocate"
/// (commit immediately, before the audit event is durably written) coupled version-sequence
/// advancement to a step (the audit write) that can independently fail. If the audit write failed
/// after a single-phase allocate had already committed, the version was permanently burned - a
/// retry of the SAME legitimate version threw <see cref="StaleComparisonVersionException"/>, and
/// skipping ahead left a permanent hole for a result that never existed. Fixed by splitting
/// allocation into a genuine two-phase RESERVE / COMMIT / RELEASE protocol:
/// <see cref="AllocateVersion"/> claims the next slot but does NOT yet advance the session's
/// committed sequence; while a reservation is outstanding for a session, no other version can be
/// reserved for that SAME session (this is what makes the protocol atomic end to end, not just at
/// the reserve step - it rules out a second caller sneaking in and reserving version 2 while
/// version 1's audit write is still in flight). <see cref="CommitVersion"/> finalizes a
/// reservation once its audit event is confirmed durable, advancing the committed sequence.
/// <see cref="ReleaseVersion"/> discards a reservation without consuming it - called when the
/// operation the reservation was protecting fails before becoming durable - so the SAME version
/// number remains legitimately reissuable. This is the same "reserve, only commit once durable,
/// release/rollback on failure" discipline <c>ITenantAuditSink</c>'s own audit-before-commit
/// ambiguous-write handling and ADR-0001 Decision 3's optimistic-concurrency CAS both apply at
/// their own layer, now applied to version-sequence allocation itself rather than just to the
/// audit write.
/// </para>
/// </summary>
public interface IVersionStore
{
    /// <summary>Attempts to reserve <paramref name="versionNumber"/> as the next version for
    /// <paramref name="documentSessionId"/>. Returns <c>true</c> only if it is exactly the current
    /// committed version + 1 (or 1, if this session has no committed comparison yet) AND no other
    /// reservation for this session is currently outstanding. A duplicate, skipped, or backward
    /// version - or a second reservation attempt while one is already outstanding for this session -
    /// returns <c>false</c> and leaves this store's state unchanged. The caller MUST eventually
    /// call <see cref="CommitVersion"/> or <see cref="ReleaseVersion"/> for a successful
    /// reservation; until then, no further version can be reserved for this session.</summary>
    bool AllocateVersion(string documentSessionId, int versionNumber);

    /// <summary>Finalizes a version previously reserved via <see cref="AllocateVersion"/>,
    /// advancing this session's committed sequence. Call ONLY after the operation the reservation
    /// was protecting (here, the comparison's audit event) is confirmed durable.</summary>
    void CommitVersion(string documentSessionId, int versionNumber);

    /// <summary>Discards a version previously reserved via <see cref="AllocateVersion"/> WITHOUT
    /// consuming it - the session's committed sequence is left exactly as it was before the
    /// reservation, so the SAME <paramref name="versionNumber"/> remains legitimately reissuable
    /// to a subsequent caller. Call when the operation the reservation was protecting fails before
    /// becoming durable.</summary>
    void ReleaseVersion(string documentSessionId, int versionNumber);
}

/// <summary>Thrown when a caller's claimed <c>versionNumber</c> is not the next expected version
/// for a document session's comparison sequence - a duplicate, skipped, or backward version, or a
/// reservation attempted while another is still outstanding for the same session.</summary>
public sealed class StaleComparisonVersionException(string message) : InvalidOperationException(message);

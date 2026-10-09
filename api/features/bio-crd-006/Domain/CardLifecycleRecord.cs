using Biolog.Features.CardLifecycle.Audit;
using Biolog.Features.CardLifecycle.Identifiers;

namespace Biolog.Features.CardLifecycle.Domain;

/// <summary>
/// One physical card's report/recovery/permanent-revocation history, keyed by the SAME natural
/// composite key BIO-CRD-005's own <c>CardIssue</c> uses -
/// <c>(TenantId, PersonId, CardClass, SequenceNumber)</c> - since this is a companion record
/// for a specific card issue, not a new aggregate of its own kind (ADR-0001 §1.6/§3.1).
///
/// Mutable and CAS-versioned (ADR-0001 §3.2), like BIO-CRD-005's own <c>CardIssue</c>: it
/// genuinely changes state (locked, recovered, permanently revoked) and AC01/AC02/AC04 each
/// depend on that transition being a real compare-and-set, not a last-write-wins overwrite.
///
/// <b>Construction is internal</b> (the same BIO-IDN-001-QA-01 lesson every mutable aggregate
/// in this codebase applies from the start), restricted to this assembly, <c>Biolog.Api</c> and
/// this project's own test assembly via <c>InternalsVisibleTo</c>.
/// </summary>
public sealed record CardLifecycleRecord
{
    public string TenantId { get; }
    public string PersonId { get; }
    public string CardClass { get; }
    public int SequenceNumber { get; }
    public required CardLockState LockState { get; init; }
    public required int Version { get; init; }

    /// <summary>AC01: "configurable reason" - a plain, non-blank, tenant-defined string, the same "business identifier, not a canonical enum" choice BIO-CRD-005's own <c>CardClass</c> made (no AC enumerates an exact taxonomy of report reasons).</summary>
    public required string ReasonCode { get; init; }
    public string? Reason { get; init; }
    public required string ReportedAt { get; init; }
    public required AuditActor ReportedBy { get; init; }

    public string? RecoveredAt { get; init; }
    public AuditActor? RecoveredBy { get; init; }

    public string? RevokedAt { get; init; }
    public AuditActor? RevokedBy { get; init; }
    public string? RevokedReasonCode { get; init; }

    /// <summary>BIO-CRD-006-QA-03: set only while <see cref="LockState"/> is <see cref="CardLockState.RevocationPending"/> - the state to compensate back to (null means "had no prior record", compensate to <see cref="CardLockState.Unlocked"/>) if the triggering activation ultimately fails. Durable on the record itself, not a local variable, so a later retry of an interrupted attempt can still compensate correctly.</summary>
    public CardLockState? PriorLockStateBeforeRevocationPending { get; init; }

    public required string CreatedAt { get; init; }
    public required string UpdatedAt { get; init; }

    internal CardLifecycleRecord(string tenantId, string personId, string cardClass, int sequenceNumber)
    {
        if (!CanonicalId.IsTenantId(tenantId))
        {
            throw new ArgumentException($"'{tenantId}' is not a well-formed tenant id (ADR-0001)", nameof(tenantId));
        }

        if (!CanonicalId.IsPersonId(personId))
        {
            throw new ArgumentException($"'{personId}' is not a well-formed person id (ADR-0001)", nameof(personId));
        }

        if (string.IsNullOrWhiteSpace(cardClass))
        {
            throw new ArgumentException("cardClass must not be blank", nameof(cardClass));
        }

        if (sequenceNumber < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(sequenceNumber), sequenceNumber, "a card issue's sequence number starts at 1");
        }

        TenantId = tenantId;
        PersonId = personId;
        CardClass = cardClass;
        SequenceNumber = sequenceNumber;
    }
}

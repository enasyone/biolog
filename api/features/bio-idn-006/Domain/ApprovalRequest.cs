namespace Biolog.Features.Rbac.Domain;

/// <summary>
/// BIO-IDN-006-AC03's maker-checker record: a MAKER's request that a sensitive action
/// (<c>ActionKey</c>, matching <see cref="SensitiveActionRule.ActionKey"/>) be allowed to
/// proceed against some <see cref="TargetRef"/> (a free-form reference to whatever the action
/// applies to - this story does not own or constrain what a target looks like), pending a
/// CHECKER's decision.
///
/// <b>No canonical id of its own kind.</b> Unlike every other aggregate raised so far in this
/// epic, an approval request has no natural UNIQUENESS key on its own - a maker can legitimately
/// raise several requests for the same <c>(TenantId, ActionKey, TargetRef)</c> over time (a
/// rejected request followed by a corrected resubmission, for instance). Rather than mint a new
/// ADR-0001 canonical identifier type (which would need a shared-contract/ADR amendment and
/// Kemi's recorded approval for a facility with no proven need yet), this mirrors BIO-IDN-003's
/// <c>InstitutionalIdentifier</c> shape exactly: natural key
/// <c>(TenantId, ActionKey, TargetRef, SequenceNumber)</c>, with AT MOST ONE
/// <see cref="ApprovalRequestStatus.Pending"/> record per <c>(TenantId, ActionKey, TargetRef)</c>
/// at any time (the structural invariant <see cref="MakerCheckerService.RequestApprovalAsync"/>
/// and <c>InMemoryApprovalRequestRepository.CreateAsync</c> both enforce) and a full, permanent
/// history of every request ever raised for that target - nothing is ever deleted or edited in
/// place.
///
/// <b>Separation of duties (AC03's core security property, and the direct target of this
/// story's mandatory privilege-escalation tests).</b> The CHECKER
/// (<see cref="DecidedByActorId"/>) may never be the same actor as the MAKER
/// (<see cref="RequestedByActorId"/>) - <see cref="MakerCheckerService.DecideAsync"/> reads the
/// pending request under the per-target lock and rejects a self-approval attempt BEFORE ANY
/// write is attempted, and <c>InMemoryApprovalRequestRepository.ReplaceAsync</c> independently
/// re-checks the same rule at the persistence boundary against the actual deciding actor it is
/// given, not anything the caller could smuggle into the replacement record itself (the
/// dual-boundary discipline every mutation surface in this epic applies). A maker-checker
/// control that a single actor can satisfy alone is not a maker-checker control at all - it is
/// the ONE property this whole aggregate exists to enforce.
///
/// <b>Consumption is single-use (BIO-IDN-006-QA-05, Seyi round 2, High).</b> An APPROVED request
/// clears exactly ONE gated mutation, never every future mutation for the same target
/// indefinitely. <see cref="ConsumedAt"/> is set atomically by
/// <see cref="MakerCheckerService.TryConsumeApprovalAsync"/> the first (and only) time an
/// authorization gate successfully uses this approval; a second attempt to use the SAME approved
/// record finds <see cref="ConsumedAt"/> already set and is rejected as
/// <c>approval_already_consumed</c>, requiring a genuinely NEW request-and-decide cycle (the next
/// <see cref="SequenceNumber"/>) rather than silently replaying the old one. This is deliberately
/// a SEPARATE fact from <see cref="Status"/> - consumption is a bookkeeping step after the
/// CHECKER's decision, not a new decision itself, so it does not disturb
/// <see cref="ApprovalRequestStatus"/>'s own closed Pending/Approved/Rejected lifecycle or
/// <see cref="MakerCheckerService.DecideAsync"/>'s "decided requests are terminal" invariant.
///
/// <b>Construction is internal</b> (BIO-IDN-001-QA-01's lesson, applied from the start).
/// </summary>
public sealed record ApprovalRequest
{
    public string TenantId { get; }
    public string ActionKey { get; }
    public string TargetRef { get; }
    public int SequenceNumber { get; }
    public required ApprovalRequestStatus Status { get; init; }
    public required string RequestedByActorId { get; init; }
    public required string RequestReason { get; init; }
    public string? DecidedByActorId { get; init; }
    public string? DecisionReasonCode { get; init; }
    public string? DecidedAt { get; init; }
    public string? ConsumedAt { get; init; }
    public required int Version { get; init; }
    public required string CreatedAt { get; init; }
    public required string UpdatedAt { get; init; }

    internal ApprovalRequest(string tenantId, string actionKey, string targetRef, int sequenceNumber)
    {
        if (!Identifiers.CanonicalId.IsTenantId(tenantId))
        {
            throw new ArgumentException($"'{tenantId}' is not a well-formed tenant id (ADR-0001)", nameof(tenantId));
        }

        if (string.IsNullOrWhiteSpace(actionKey))
        {
            throw new ArgumentException("actionKey must not be blank", nameof(actionKey));
        }

        if (string.IsNullOrWhiteSpace(targetRef))
        {
            throw new ArgumentException("targetRef must not be blank", nameof(targetRef));
        }

        if (sequenceNumber < 1)
        {
            throw new ArgumentException("sequenceNumber must be at least 1", nameof(sequenceNumber));
        }

        TenantId = tenantId;
        ActionKey = actionKey;
        TargetRef = targetRef;
        SequenceNumber = sequenceNumber;
    }
}

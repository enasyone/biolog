namespace Biolog.Features.Rbac.Domain;

/// <summary>
/// Storage for <see cref="ApprovalRequest"/>, keyed by
/// <c>(TenantId, ActionKey, TargetRef, SequenceNumber)</c>. <c>internal</c> from the start.
/// Deliberately has NO delete/remove method (AC03's full-history ethos - see
/// <see cref="ApprovalRequest"/>'s header). <see cref="InMemoryApprovalRequestRepository"/>
/// independently re-validates identity immutability, initial state, and the maker/checker
/// separation-of-duties rule at the persistence boundary - not merely trusting
/// <see cref="MakerCheckerService"/>'s own pre-lock check.
/// </summary>
internal interface IApprovalRequestRepository
{
    Task<ApprovalRequest?> GetAsync(string tenantId, string actionKey, string targetRef, int sequenceNumber, CancellationToken cancellationToken = default);

    /// <summary>Every request ever raised for this exact (action_key, target_ref), oldest first - AC03's full history.</summary>
    Task<IReadOnlyList<ApprovalRequest>> ListHistoryAsync(string tenantId, string actionKey, string targetRef, CancellationToken cancellationToken = default);

    Task<ApprovalRequest> CreateAsync(ApprovalRequest request, CancellationToken cancellationToken = default);

    /// <summary>Compare-and-set against <paramref name="expected"/> (ADR-0001 Decision 3.2). <paramref name="decidingActorId"/> is independently re-checked against <paramref name="expected"/>'s maker - see class header.</summary>
    Task<ApprovalRequest> ReplaceAsync(ApprovalRequest expected, ApprovalRequest updated, string decidingActorId, CancellationToken cancellationToken = default);

    /// <summary>
    /// BIO-IDN-006-QA-05: marks an APPROVED, not-yet-consumed request as consumed - a genuinely
    /// separate operation from <see cref="ReplaceAsync"/> (no maker/checker identity check
    /// applies here; the actor performing the gated mutation may legitimately be either party).
    /// Independently re-validates <paramref name="expected"/>.<c>Status</c> is
    /// <see cref="ApprovalRequestStatus.Approved"/> and <c>ConsumedAt</c> is still null at the
    /// persistence boundary, plus the same CAS-via-version discipline every mutation here uses -
    /// not merely trusting <see cref="MakerCheckerService.TryConsumeApprovalAsync"/>'s own
    /// pre-lock check.
    /// </summary>
    Task<ApprovalRequest> ConsumeAsync(ApprovalRequest expected, ApprovalRequest updated, CancellationToken cancellationToken = default);
}

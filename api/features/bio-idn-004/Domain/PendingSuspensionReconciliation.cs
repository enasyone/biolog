namespace Biolog.Features.PersonLifecycle.Domain;

/// <summary>
/// BIO-IDN-004-QA-01 (Seyi round 3, High). A durable record that suspension
/// <see cref="CredentialAccessRevocationEvent.SuspendedAtVersion"/> for <see cref="PersonId"/>
/// has NOT yet had its revocation event confirmed published.
///
/// <b>Why this cannot be derived from the person's CURRENT status.</b> Round 3's fix made "is
/// reconciliation pending" a question answered by checking whether the person's CURRENT status
/// was still SUSPENDED. That broke the moment a permissioned reactivation succeeded: the person
/// moved to ACTIVE, the status-derived check silently started reporting "not pending" even
/// though THIS suspension's revocation event was never recorded, and the only reconciliation
/// path (a SUSPENDED -&gt; SUSPENDED request) became unreachable - the missing revocation
/// became permanently unreported and unrecoverable, the exact class of gap round 1 already
/// found once. A pending fact about a SPECIFIC PAST suspension occurrence must be tracked as
/// its own durable record, independent of whatever the person's status becomes afterward -
/// this type, and <see cref="IPendingSuspensionReconciliationStore"/>, are that record.
/// Reconciliation is therefore a SEPARATE operation
/// (<see cref="PersonLifecycleService.ReconcilePendingRevocationsAsync"/>), reachable regardless
/// of the person's current status, not tied to any particular transition request shape.
/// </summary>
public sealed record PendingSuspensionReconciliation
{
    public required string TenantId { get; init; }
    public required string PersonId { get; init; }
    public required int SuspendedAtVersion { get; init; }
    public string? Reason { get; init; }
    public required string RequestedByActorId { get; init; }
    public required string CorrelationId { get; init; }
    public required string RecordedAt { get; init; }
}

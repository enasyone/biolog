namespace Biolog.Features.Visitors.Domain;

/// <summary>
/// The legality tables <see cref="VisitorRecordService"/> checks before calling a
/// <see cref="VisitorRecord"/> mutation method, and <see cref="InMemoryVisitorRecordRepository"/>
/// independently re-checks at the persistence boundary - the same dual-boundary discipline
/// BIO-IDN-001's <c>PersonLifecycleTransitions"</c>/<c>InMemoryPersonRepository</c> pair
/// established. Both of this aggregate's state machines are one-directional and terminal, so a
/// simple ordinal check suffices rather than a full transition-pair table.
/// </summary>
public static class VisitorTransitions
{
    /// <summary>AC02: only <see cref="HostApprovalStatus.Pending"/> may move, and only once.</summary>
    public static bool IsAllowedApprovalTransition(HostApprovalStatus from, HostApprovalStatus to) =>
        from == HostApprovalStatus.Pending && to is HostApprovalStatus.Approved or HostApprovalStatus.Denied;

    /// <summary>Monotonic: Preregistered -> CheckedIn -> CheckedOut, never backwards, never skipped.</summary>
    public static bool IsAllowedVisitTransition(VisitStatus from, VisitStatus to) =>
        (from, to) switch
        {
            (VisitStatus.Preregistered, VisitStatus.CheckedIn) => true,
            (VisitStatus.CheckedIn, VisitStatus.CheckedOut) => true,
            _ => false,
        };
}

namespace Biolog.Features.PersonLifecycle.Domain;

/// <summary>
/// BIO-IDN-004-AC01: "Allowed identity transitions are explicit." BIO-IDN-001's own
/// <c>Person.cs</c> documents this baseline table as "deliberately conservative" and names
/// BIO-IDN-004 as the story that owns narrowing/finalizing it - this table IS that ownership,
/// expressed as a closed, explicit whitelist this project can enforce fail-fast, BEFORE ever
/// calling into <see cref="IPersonLifecycleGateway"/>. It intentionally mirrors BIO-IDN-001's
/// own <c>PersonLifecycleTransitions</c> table exactly (see that project's own baseline - this
/// project has no <c>ProjectReference</c> to read it programmatically, so a human-verified
/// mirror is what "explicit and owned by this story" means in practice). BIO-IDN-001's own
/// <c>PersonService.UpdateStatusAsync</c> re-validates the SAME rule independently at the real
/// source of truth when <see cref="IPersonLifecycleGateway.ChangeStatusAsync"/> is called - so
/// even if these two tables ever drifted, the result is a safe rejection (an
/// <c>invalid_status_transition</c> from the gateway), never a silent bypass.
///
/// <c>ARCHIVED</c> is terminal - no target is ever allowed from it, matching AC04's own "a
/// person is never truly deleted, only reaches an end state" ethos.
/// </summary>
public static class LifecycleTransitionPolicy
{
    private static readonly IReadOnlyDictionary<PersonStatus, IReadOnlyCollection<PersonStatus>> AllowedTargets =
        new Dictionary<PersonStatus, IReadOnlyCollection<PersonStatus>>
        {
            [PersonStatus.PreEnrolled] = [PersonStatus.Verified, PersonStatus.Archived],
            [PersonStatus.Verified] = [PersonStatus.Active, PersonStatus.Archived],
            [PersonStatus.Active] = [PersonStatus.Suspended, PersonStatus.Expired, PersonStatus.FormerAlumni, PersonStatus.Revoked, PersonStatus.Archived],
            [PersonStatus.Suspended] = [PersonStatus.Active, PersonStatus.Revoked, PersonStatus.Archived],
            [PersonStatus.Expired] = [PersonStatus.Active, PersonStatus.Archived],
            [PersonStatus.FormerAlumni] = [PersonStatus.Active, PersonStatus.Archived],
            [PersonStatus.Revoked] = [PersonStatus.Archived],
            [PersonStatus.Archived] = [],
        };

    public static bool IsAllowed(PersonStatus from, PersonStatus to) =>
        AllowedTargets.TryGetValue(from, out var targets) && targets.Contains(to);

    /// <summary>BIO-IDN-004-AC02: the only transition this story treats as an "emergency suspension" - ACTIVE is the only state that can reach SUSPENDED.</summary>
    public static bool IsEmergencySuspension(PersonStatus from, PersonStatus to) =>
        from == PersonStatus.Active && to == PersonStatus.Suspended;

    /// <summary>BIO-IDN-004-AC03: the only transition this story treats as a "reactivation" - SUSPENDED is the only state ACTIVE is reached from that requires permission and a reason (the VERIFIED -&gt; ACTIVE initial activation does not).</summary>
    public static bool IsReactivation(PersonStatus from, PersonStatus to) =>
        from == PersonStatus.Suspended && to == PersonStatus.Active;
}

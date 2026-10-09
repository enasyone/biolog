namespace Biolog.Features.AttendanceSessions.Domain;

/// <summary>
/// AC01/AC03/AC04's combined read model for one roster member: the computed classification
/// (never mutated) AND the latest manual adjustment, if any (also never mutated) - both shown
/// side by side, never one silently replacing the other. Built by
/// <see cref="SessionAttendanceQueryService"/>; not itself stored anywhere.
/// </summary>
public sealed record SessionRosterMemberView
{
    public required string PersonId { get; init; }
    public required SessionMemberClassification Classification { get; init; }
    public required AttendanceAdjustment? LatestAdjustment { get; init; }

    /// <summary>The status a reader/downstream consumer should treat as authoritative right now - the adjustment's <see cref="AttendanceAdjustment.AdjustedStatus"/> if one exists, otherwise <see cref="Classification"/>'s own computed status. Never persisted; recomputed on every read.</summary>
    public SessionAttendanceStatusValue EffectiveStatus => LatestAdjustment?.AdjustedStatus ?? Classification.Status;
}

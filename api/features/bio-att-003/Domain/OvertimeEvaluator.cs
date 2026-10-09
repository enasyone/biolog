namespace Biolog.Features.WorkforceAttendance.Domain;

/// <summary>
/// AC03 - overtime is DERIVED, never a raw-event mutation: a pure function of already-validated
/// <see cref="WorkforceClockPair"/>s plus a <see cref="WorkforceShift"/>'s own configured
/// duration. Total worked minutes across every <see cref="ClockPairStatus.Complete"/> pair that
/// day, minus the shift's own <see cref="WorkforceShift.ExpectedDurationMinutes"/>, floored at
/// zero (a short day is simply not overtime, never a negative value). Irregular pairs
/// (<see cref="ClockPairStatus.OpenCheckIn"/>/<see cref="ClockPairStatus.UnmatchedCheckOut"/>/
/// <see cref="ClockPairStatus.Overlapping"/>) contribute no duration - AC02's own validation
/// already surfaces them separately; this evaluator does not also guess a duration for them.
/// </summary>
public static class OvertimeEvaluator
{
    public static int EvaluateMinutes(IReadOnlyList<WorkforceClockPair> pairs, WorkforceShift shift)
    {
        var workedMinutes = pairs
            .Where(p => p.Status == ClockPairStatus.Complete)
            .Sum(p => p.DurationMinutes ?? 0);

        return Math.Max(0, workedMinutes - shift.ExpectedDurationMinutes);
    }
}

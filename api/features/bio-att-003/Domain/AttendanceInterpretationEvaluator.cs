using Biolog.Features.WorkforceAttendance.Identifiers;

namespace Biolog.Features.WorkforceAttendance.Domain;

/// <summary>
/// BIO-ATT-003-F01 (High, Seyi round 1): "the approved user story requires clock-in/out, shift,
/// lateness, overtime and absence interpretation... a first check-in after StartTime +
/// GraceMinutes is therefore indistinguishable from an on-time arrival, and a day with no clock
/// events is merely an empty pair list rather than an absence interpretation." A pure function,
/// the same discipline <see cref="ClockPairEvaluator"/>/<see cref="OvertimeEvaluator"/> already
/// hold themselves to: the same <paramref name="dayEvents"/>/<paramref name="shift"/> always
/// produces the same interpretation, no I/O, no mutation.
///
/// <b>Why this reads raw events directly, not <see cref="WorkforceClockPair"/>.</b> Lateness is
/// about WHEN the day's first CheckIn happened, not whether it ever paired with a CheckOut - an
/// <see cref="ClockPairStatus.OpenCheckIn"/> (forgot to clock out) or
/// <see cref="ClockPairStatus.Overlapping"/> pairing still has a real, meaningful arrival time to
/// judge lateness against. Re-deriving the first CheckIn from <paramref name="dayEvents"/>
/// directly (the same input <see cref="ClockPairEvaluator.Evaluate"/> takes) avoids coupling this
/// evaluator to the OTHER evaluator's own derived shape for a question pairing was never meant to
/// answer.
///
/// <b>Overnight-shift arrival math (BIO-ATT-003-F01 round-2 remediation, SY-ATT003-03, High,
/// Seyi round 2).</b> Round 1's fix applied an UNCONDITIONAL modulo-1440 to arrival-minus-start,
/// so a genuinely EARLY same-day arrival (07:55 against an 08:00 start + 10 min grace, delta
/// -15) wrapped to 1425 minutes "late" instead of being recognised as on-time. The wrap is only
/// ever correct for an OVERNIGHT shift's own early-morning continuation (a 00:10 arrival against
/// a 22:00 start is genuinely 130 minutes after start, not "10 minutes before midnight"); for a
/// same-day shift there is nothing to wrap across at all - a negative delta is simply an early
/// arrival, full stop.
///
/// Even restricted to overnight shifts, a bare "wrap whenever negative" rule is still wrong: an
/// overnight shift can ALSO have a genuinely early arrival the same evening before its own start
/// (21:50 against a 22:00-06:00 shift, delta -10) - clock-time-of-day alone cannot tell "ten
/// minutes before tonight's start" apart from "ten minutes before tonight's start, interpreted
/// as the unwrapped reading of an after-midnight arrival" without more information than this
/// evaluator has. Resolved by picking whichever of the two readings (unwrapped delta, or
/// delta+1440) has the SMALLER absolute value - the interpretation closer to zero is the more
/// plausible one: -10 stays -10 (early, not a near-full-day wrap to 1430); -1300 becomes 140
/// (the overnight continuation, not an implausible 1300-minutes-early reading). Equivalent to
/// wrapping only when the unwrapped delta is more negative than -720 (half of 1440 - the
/// crossover point where wrapping is strictly closer to zero).
///
/// Disclosed limitation, inherited from <see cref="WorkforceShift"/>'s own existing "wall time,
/// no timezone, no full calendar awareness" simplification: an arrival genuinely many hours
/// removed from a plausible same-shift reading (not a realistic clock-in scenario for a single
/// calendar date's own daily summary) could still pick the less-intuitive of the two readings
/// right at the 720-minute crossover - the same category of simplification this feature already
/// carries, not a new one introduced here.
/// </summary>
public static class AttendanceInterpretationEvaluator
{
    /// <summary><paramref name="dayEvents"/> must already be filtered to one (tenant, person, calendar date) - the same caller contract <see cref="ClockPairEvaluator.Evaluate"/> states for itself.</summary>
    public static WorkforceAttendanceInterpretation Evaluate(IReadOnlyList<ClockEventView> dayEvents, WorkforceShift shift)
    {
        var firstCheckIn = dayEvents
            .Where(e => e.Mode == ClockMode.CheckIn)
            .OrderBy(e => e.OccurredAt, StringComparer.Ordinal) // RFC 3339 UTC sorts lexicographically == chronologically
            .FirstOrDefault();

        if (firstCheckIn is null)
        {
            return new WorkforceAttendanceInterpretation { Status = AttendanceInterpretationStatus.Absent };
        }

        UtcInstant.TryParse(firstCheckIn.OccurredAt, out var arrival);
        var arrivalMinutes = (arrival.Hour * 60) + arrival.Minute;

        var start = TimeOnly.ParseExact(shift.StartTime, "HH:mm");
        var startMinutes = (start.Hour * 60) + start.Minute;
        var end = TimeOnly.ParseExact(shift.EndTime, "HH:mm");
        var isOvernightShift = end <= start; // mirrors WorkforceShift.ExpectedDurationMinutes's own wrap condition

        var minutesAfterStart = arrivalMinutes - startMinutes;

        // Only an overnight shift can ever plausibly need the wrapped reading at all (a same-day
        // shift has nothing to wrap across) - and even then, only when the wrapped reading is
        // genuinely closer to zero (more plausible) than the unwrapped one. -720 is the exact
        // crossover: more negative than that, wrapping (+1440) lands closer to zero.
        if (isOvernightShift && minutesAfterStart < -720)
        {
            minutesAfterStart += 1440;
        }

        if (minutesAfterStart <= shift.GraceMinutes)
        {
            return new WorkforceAttendanceInterpretation { Status = AttendanceInterpretationStatus.OnTime, FirstCheckInEventId = firstCheckIn.EventId };
        }

        return new WorkforceAttendanceInterpretation
        {
            Status = AttendanceInterpretationStatus.Late,
            FirstCheckInEventId = firstCheckIn.EventId,
            MinutesLate = minutesAfterStart - shift.GraceMinutes,
        };
    }
}

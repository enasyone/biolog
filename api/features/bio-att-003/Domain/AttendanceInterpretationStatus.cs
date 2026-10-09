namespace Biolog.Features.WorkforceAttendance.Domain;

/// <summary>
/// AC01/round-1 remediation (BIO-ATT-003-F01, High): the approved user story requires
/// "clock-in/out, shift, lateness, overtime and absence interpretation" - this is the lateness
/// and absence half, derived from a shift's own <see cref="WorkforceShift.StartTime"/>/
/// <see cref="WorkforceShift.GraceMinutes"/> and the day's validated clock pairs, never from a
/// raw event mutation. See <see cref="AttendanceInterpretationEvaluator"/>.
/// </summary>
public enum AttendanceInterpretationStatus
{
    /// <summary>The first CheckIn of the day arrived at or before the shift's own start time plus its grace period.</summary>
    OnTime,

    /// <summary>The first CheckIn of the day arrived strictly after the shift's own start time plus its grace period.</summary>
    Late,

    /// <summary>No CheckIn event exists for this person on this date at all - never inferred from an incomplete pairing (an <see cref="ClockPairStatus.OpenCheckIn"/>/<see cref="ClockPairStatus.UnmatchedCheckOut"/> day is NOT absent, since a CheckIn genuinely happened).</summary>
    Absent,
}

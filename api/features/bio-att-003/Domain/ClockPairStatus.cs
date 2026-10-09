namespace Biolog.Features.WorkforceAttendance.Domain;

/// <summary>
/// AC02 - the result of validating one chronological clock-in/clock-out pairing. A pair is never
/// silently guessed or merged; an irregular pairing is surfaced as its own named status, not
/// absorbed into <see cref="ClockPairStatus.Complete"/>.
/// </summary>
public enum ClockPairStatus
{
    /// <summary>A CheckIn immediately followed, chronologically, by a CheckOut - a genuine worked interval.</summary>
    Complete,

    /// <summary>A CheckIn with no CheckOut yet recorded for this date - still clocked in (or the person forgot to clock out and no correction has been made).</summary>
    OpenCheckIn,

    /// <summary>A CheckOut with no preceding open CheckIn to close - e.g. a duplicate or mis-captured event.</summary>
    UnmatchedCheckOut,

    /// <summary>A second CheckIn occurred before the prior one was closed by a CheckOut - never silently treated as closing the prior pair.</summary>
    Overlapping,
}

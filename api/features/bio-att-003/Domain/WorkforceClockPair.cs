namespace Biolog.Features.WorkforceAttendance.Domain;

/// <summary>One validated clock-pairing result (AC02) - never persisted; always recomputed at read time from the raw events <see cref="IAttendanceEventGateway"/> returns, the same "derived, never raw-event mutation" discipline AC03 states explicitly for overtime.</summary>
public sealed record WorkforceClockPair
{
    public required ClockPairStatus Status { get; init; }

    /// <summary><c>null</c> only for <see cref="ClockPairStatus.UnmatchedCheckOut"/> - there is no check-in to name.</summary>
    public string? CheckInEventId { get; init; }

    /// <summary><c>null</c> for <see cref="ClockPairStatus.OpenCheckIn"/> and <see cref="ClockPairStatus.Overlapping"/> - there is no (or not yet this pairing's own) check-out to name.</summary>
    public string? CheckOutEventId { get; init; }

    /// <summary>Only present when <see cref="Status"/> is <see cref="ClockPairStatus.Complete"/> - an irregular pairing has no defensible duration to report.</summary>
    public int? DurationMinutes { get; init; }
}

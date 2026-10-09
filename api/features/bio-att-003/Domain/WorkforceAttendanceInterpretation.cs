namespace Biolog.Features.WorkforceAttendance.Domain;

/// <summary>
/// BIO-ATT-003-F01 (High) - one day's lateness/absence outcome, always recomputed at read time
/// from the day's raw clock events and the selected shift, never persisted and never mutating a
/// raw event (the same discipline <see cref="WorkforceClockPair"/>/overtime already establish).
/// </summary>
public sealed record WorkforceAttendanceInterpretation
{
    public required AttendanceInterpretationStatus Status { get; init; }

    /// <summary><c>null</c> only for <see cref="AttendanceInterpretationStatus.Absent"/> - there is no CheckIn to name.</summary>
    public string? FirstCheckInEventId { get; init; }

    /// <summary>Only present when <see cref="Status"/> is <see cref="AttendanceInterpretationStatus.Late"/> - minutes strictly beyond the shift's own start time plus grace period.</summary>
    public int? MinutesLate { get; init; }
}

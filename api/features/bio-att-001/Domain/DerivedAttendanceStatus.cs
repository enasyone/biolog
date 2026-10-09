namespace Biolog.Features.Attendance.Domain;

/// <summary>
/// AC02's derived side - the OUTPUT of <see cref="AttendanceStatusEvaluator"/>, never a stored
/// aggregate and never written back onto any <see cref="AttendanceEvent"/>. <see cref="BasedOnEventIds"/>
/// names exactly which raw events produced this classification, so a caller (or a later audit)
/// can trace a derived status back to the raw facts it came from without the two ever being
/// conflated into one record.
/// </summary>
public sealed record DerivedAttendanceStatus
{
    public required string TenantId { get; init; }
    public required string PersonId { get; init; }

    /// <summary>Calendar date (yyyy-MM-dd) this status was computed for.</summary>
    public required string Date { get; init; }

    public required AttendanceStatusValue Status { get; init; }
    public required IReadOnlyList<string> BasedOnEventIds { get; init; }
    public required int PolicyVersion { get; init; }
    public required string ComputedAt { get; init; }
}

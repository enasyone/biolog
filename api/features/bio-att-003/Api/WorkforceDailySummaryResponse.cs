using System.Text.Json.Serialization;
using Biolog.Features.WorkforceAttendance.Domain;

namespace Biolog.Features.WorkforceAttendance.Api;

public sealed record WorkforceClockPairResponse
{
    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("check_in_event_id")]
    public string? CheckInEventId { get; init; }

    [JsonPropertyName("check_out_event_id")]
    public string? CheckOutEventId { get; init; }

    [JsonPropertyName("duration_minutes")]
    public int? DurationMinutes { get; init; }

    public static WorkforceClockPairResponse From(WorkforceClockPair pair) => new()
    {
        Status = pair.Status switch
        {
            ClockPairStatus.Complete => "complete",
            ClockPairStatus.OpenCheckIn => "open_check_in",
            ClockPairStatus.UnmatchedCheckOut => "unmatched_check_out",
            ClockPairStatus.Overlapping => "overlapping",
            _ => throw new ArgumentOutOfRangeException(nameof(pair), pair.Status, "unreachable: exhaustive switch"),
        },
        CheckInEventId = pair.CheckInEventId,
        CheckOutEventId = pair.CheckOutEventId,
        DurationMinutes = pair.DurationMinutes,
    };
}

public sealed record WorkforceAttendanceInterpretationResponse
{
    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("first_check_in_event_id")]
    public string? FirstCheckInEventId { get; init; }

    [JsonPropertyName("minutes_late")]
    public int? MinutesLate { get; init; }

    public static WorkforceAttendanceInterpretationResponse From(WorkforceAttendanceInterpretation interpretation) => new()
    {
        Status = interpretation.Status switch
        {
            AttendanceInterpretationStatus.OnTime => "on_time",
            AttendanceInterpretationStatus.Late => "late",
            AttendanceInterpretationStatus.Absent => "absent",
            _ => throw new ArgumentOutOfRangeException(nameof(interpretation), interpretation.Status, "unreachable: exhaustive switch"),
        },
        FirstCheckInEventId = interpretation.FirstCheckInEventId,
        MinutesLate = interpretation.MinutesLate,
    };
}

public sealed record WorkforceAttendanceAdjustmentResponse
{
    [JsonPropertyName("adjustment_id")]
    public required string AdjustmentId { get; init; }

    [JsonPropertyName("original_overtime_minutes")]
    public required int OriginalOvertimeMinutes { get; init; }

    [JsonPropertyName("adjusted_overtime_minutes")]
    public required int AdjustedOvertimeMinutes { get; init; }

    [JsonPropertyName("reason")]
    public required string Reason { get; init; }

    [JsonPropertyName("adjusted_by_actor_id")]
    public required string AdjustedByActorId { get; init; }

    [JsonPropertyName("adjusted_at")]
    public required string AdjustedAt { get; init; }

    public static WorkforceAttendanceAdjustmentResponse From(WorkforceAttendanceAdjustment adjustment) => new()
    {
        AdjustmentId = adjustment.AdjustmentId,
        OriginalOvertimeMinutes = adjustment.OriginalOvertimeMinutes,
        AdjustedOvertimeMinutes = adjustment.AdjustedOvertimeMinutes,
        Reason = adjustment.Reason,
        AdjustedByActorId = adjustment.AdjustedByActorId,
        AdjustedAt = adjustment.AdjustedAt,
    };
}

public sealed record WorkforceDailySummaryResponse
{
    [JsonPropertyName("tenant_id")]
    public required string TenantId { get; init; }

    [JsonPropertyName("person_id")]
    public required string PersonId { get; init; }

    [JsonPropertyName("date")]
    public required string Date { get; init; }

    [JsonPropertyName("shift_id")]
    public required string ShiftId { get; init; }

    [JsonPropertyName("pairs")]
    public required IReadOnlyList<WorkforceClockPairResponse> Pairs { get; init; }

    [JsonPropertyName("computed_overtime_minutes")]
    public required int ComputedOvertimeMinutes { get; init; }

    [JsonPropertyName("effective_overtime_minutes")]
    public required int EffectiveOvertimeMinutes { get; init; }

    [JsonPropertyName("interpretation")]
    public required WorkforceAttendanceInterpretationResponse Interpretation { get; init; }

    [JsonPropertyName("adjustment")]
    public WorkforceAttendanceAdjustmentResponse? Adjustment { get; init; }

    public static WorkforceDailySummaryResponse From(WorkforceDailySummary summary) => new()
    {
        TenantId = summary.TenantId,
        PersonId = summary.PersonId,
        Date = summary.Date,
        ShiftId = summary.ShiftId,
        Pairs = [.. summary.Pairs.Select(WorkforceClockPairResponse.From)],
        ComputedOvertimeMinutes = summary.ComputedOvertimeMinutes,
        EffectiveOvertimeMinutes = summary.EffectiveOvertimeMinutes,
        Interpretation = WorkforceAttendanceInterpretationResponse.From(summary.Interpretation),
        Adjustment = summary.Adjustment is null ? null : WorkforceAttendanceAdjustmentResponse.From(summary.Adjustment),
    };
}

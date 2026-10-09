using System.Text.Json.Serialization;

namespace Biolog.Features.WorkforceAttendance.Api;

public sealed record CreateWorkforceShiftRequest
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("start_time")]
    public required string StartTime { get; init; }

    [JsonPropertyName("end_time")]
    public required string EndTime { get; init; }

    [JsonPropertyName("grace_minutes")]
    public int GraceMinutes { get; init; }
}

public sealed record UpdateWorkforceShiftRequest
{
    [JsonPropertyName("version")]
    public required int Version { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("start_time")]
    public required string StartTime { get; init; }

    [JsonPropertyName("end_time")]
    public required string EndTime { get; init; }

    [JsonPropertyName("grace_minutes")]
    public int GraceMinutes { get; init; }
}

public sealed record CreateWorkforceAttendanceAdjustmentRequest
{
    [JsonPropertyName("shift_id")]
    public required string ShiftId { get; init; }

    [JsonPropertyName("adjusted_overtime_minutes")]
    public required int AdjustedOvertimeMinutes { get; init; }

    [JsonPropertyName("reason")]
    public required string Reason { get; init; }
}

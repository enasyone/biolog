using System.Text.Json.Serialization;
using Biolog.Features.WorkforceAttendance.Domain;

namespace Biolog.Features.WorkforceAttendance.Api;

public sealed record WorkforceShiftResponse
{
    [JsonPropertyName("shift_id")]
    public required string ShiftId { get; init; }

    [JsonPropertyName("tenant_id")]
    public required string TenantId { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("start_time")]
    public required string StartTime { get; init; }

    [JsonPropertyName("end_time")]
    public required string EndTime { get; init; }

    [JsonPropertyName("grace_minutes")]
    public required int GraceMinutes { get; init; }

    [JsonPropertyName("expected_duration_minutes")]
    public required int ExpectedDurationMinutes { get; init; }

    [JsonPropertyName("version")]
    public required int Version { get; init; }

    [JsonPropertyName("created_at")]
    public required string CreatedAt { get; init; }

    [JsonPropertyName("updated_at")]
    public required string UpdatedAt { get; init; }

    [JsonPropertyName("updated_by")]
    public required string UpdatedBy { get; init; }

    public static WorkforceShiftResponse From(WorkforceShift shift) => new()
    {
        ShiftId = shift.ShiftId,
        TenantId = shift.TenantId,
        Name = shift.Name,
        StartTime = shift.StartTime,
        EndTime = shift.EndTime,
        GraceMinutes = shift.GraceMinutes,
        ExpectedDurationMinutes = shift.ExpectedDurationMinutes,
        Version = shift.Version,
        CreatedAt = shift.CreatedAt,
        UpdatedAt = shift.UpdatedAt,
        UpdatedBy = shift.UpdatedBy,
    };
}

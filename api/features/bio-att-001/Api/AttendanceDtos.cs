using System.Text.Json.Serialization;
using Biolog.Features.Attendance.Domain;

namespace Biolog.Features.Attendance.Api;

// --- Request DTOs (wire shape only - the services own all parsing/validation) ---

public sealed record VerificationEvidenceRequest
{
    [JsonPropertyName("method")]
    public string? Method { get; init; }

    [JsonPropertyName("verified")]
    public bool Verified { get; init; }

    [JsonPropertyName("confidence")]
    public double? Confidence { get; init; }
}

public sealed record CaptureAttendanceEventRequest
{
    [JsonPropertyName("person_id")]
    public string PersonId { get; init; } = "";

    [JsonPropertyName("source")]
    public string? Source { get; init; }

    [JsonPropertyName("device_id")]
    public string? DeviceId { get; init; }

    [JsonPropertyName("location")]
    public string? Location { get; init; }

    [JsonPropertyName("mode")]
    public string? Mode { get; init; }

    [JsonPropertyName("evidence")]
    public VerificationEvidenceRequest Evidence { get; init; } = new();

    [JsonPropertyName("occurred_at")]
    public string? OccurredAt { get; init; }

    [JsonPropertyName("idempotency_key")]
    public string? IdempotencyKey { get; init; }
}

public sealed record SetAttendancePolicyRequest
{
    [JsonPropertyName("interpretation_mode")]
    public string? InterpretationMode { get; init; }

    [JsonPropertyName("expected_start_time")]
    public string? ExpectedStartTime { get; init; }

    [JsonPropertyName("late_grace_minutes")]
    public int LateGraceMinutes { get; init; }
}

// --- Response DTOs ---

public sealed record VerificationEvidenceResponse
{
    [JsonPropertyName("method")]
    public required string Method { get; init; }

    [JsonPropertyName("verified")]
    public required bool Verified { get; init; }

    [JsonPropertyName("confidence")]
    public double? Confidence { get; init; }

    public static VerificationEvidenceResponse From(VerificationEvidence evidence) => new()
    {
        Method = evidence.Method.ToWireString(),
        Verified = evidence.Verified,
        Confidence = evidence.Confidence,
    };
}

public sealed record AttendanceEventResponse
{
    [JsonPropertyName("event_id")]
    public required string EventId { get; init; }

    [JsonPropertyName("tenant_id")]
    public required string TenantId { get; init; }

    [JsonPropertyName("person_id")]
    public required string PersonId { get; init; }

    [JsonPropertyName("source")]
    public required string Source { get; init; }

    [JsonPropertyName("device_id")]
    public string? DeviceId { get; init; }

    [JsonPropertyName("location")]
    public string? Location { get; init; }

    [JsonPropertyName("mode")]
    public required string Mode { get; init; }

    [JsonPropertyName("evidence")]
    public required VerificationEvidenceResponse Evidence { get; init; }

    [JsonPropertyName("occurred_at")]
    public required string OccurredAt { get; init; }

    [JsonPropertyName("received_at")]
    public required string ReceivedAt { get; init; }

    [JsonPropertyName("idempotency_key")]
    public required string IdempotencyKey { get; init; }

    public static AttendanceEventResponse From(AttendanceEvent e) => new()
    {
        EventId = e.EventId,
        TenantId = e.TenantId,
        PersonId = e.PersonId,
        Source = e.Source.ToWireString(),
        DeviceId = e.DeviceId,
        Location = e.Location,
        Mode = e.Mode.ToWireString(),
        Evidence = VerificationEvidenceResponse.From(e.Evidence),
        OccurredAt = e.OccurredAt,
        ReceivedAt = e.ReceivedAt,
        IdempotencyKey = e.IdempotencyKey,
    };
}

public sealed record AttendanceEventListResponse
{
    [JsonPropertyName("data")]
    public required IReadOnlyList<AttendanceEventResponse> Data { get; init; }
}

public sealed record DerivedAttendanceStatusResponse
{
    [JsonPropertyName("tenant_id")]
    public required string TenantId { get; init; }

    [JsonPropertyName("person_id")]
    public required string PersonId { get; init; }

    [JsonPropertyName("date")]
    public required string Date { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("based_on_event_ids")]
    public required IReadOnlyList<string> BasedOnEventIds { get; init; }

    [JsonPropertyName("policy_version")]
    public required int PolicyVersion { get; init; }

    [JsonPropertyName("computed_at")]
    public required string ComputedAt { get; init; }

    public static DerivedAttendanceStatusResponse From(DerivedAttendanceStatus status) => new()
    {
        TenantId = status.TenantId,
        PersonId = status.PersonId,
        Date = status.Date,
        Status = status.Status.ToWireString(),
        BasedOnEventIds = status.BasedOnEventIds,
        PolicyVersion = status.PolicyVersion,
        ComputedAt = status.ComputedAt,
    };
}

public sealed record AttendancePolicyResponse
{
    [JsonPropertyName("tenant_id")]
    public required string TenantId { get; init; }

    [JsonPropertyName("interpretation_mode")]
    public required string InterpretationMode { get; init; }

    [JsonPropertyName("expected_start_time")]
    public string? ExpectedStartTime { get; init; }

    [JsonPropertyName("late_grace_minutes")]
    public required int LateGraceMinutes { get; init; }

    [JsonPropertyName("version")]
    public required int Version { get; init; }

    [JsonPropertyName("updated_at")]
    public required string UpdatedAt { get; init; }

    [JsonPropertyName("updated_by")]
    public required string UpdatedBy { get; init; }

    public static AttendancePolicyResponse From(AttendancePolicy policy) => new()
    {
        TenantId = policy.TenantId,
        InterpretationMode = policy.InterpretationMode.ToWireString(),
        ExpectedStartTime = policy.ExpectedStartTime,
        LateGraceMinutes = policy.LateGraceMinutes,
        Version = policy.Version,
        UpdatedAt = policy.UpdatedAt,
        UpdatedBy = policy.UpdatedBy,
    };
}

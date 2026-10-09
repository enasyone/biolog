using System.Text.Json.Serialization;
using Biolog.Features.AttendanceSessions.Domain;

namespace Biolog.Features.AttendanceSessions.Api;

// --- Request DTOs (wire shape only - the services own all parsing/validation) ---

public sealed record CreateSessionRequest
{
    [JsonPropertyName("session_type")]
    public string? SessionType { get; init; }

    [JsonPropertyName("title")]
    public string Title { get; init; } = "";

    [JsonPropertyName("scheduled_date")]
    public string ScheduledDate { get; init; } = "";

    [JsonPropertyName("scheduled_start_time")]
    public string ScheduledStartTime { get; init; } = "";

    [JsonPropertyName("roster_person_ids")]
    public IReadOnlyList<string> RosterPersonIds { get; init; } = [];
}

public sealed record UpdateSessionRosterRequest
{
    [JsonPropertyName("expected_version")]
    public int ExpectedVersion { get; init; }

    [JsonPropertyName("roster_person_ids")]
    public IReadOnlyList<string> RosterPersonIds { get; init; } = [];
}

public sealed record SetSessionAttendancePolicyRequest
{
    [JsonPropertyName("late_grace_minutes")]
    public int LateGraceMinutes { get; init; }

    [JsonPropertyName("absent_after_minutes")]
    public int AbsentAfterMinutes { get; init; }
}

public sealed record CreateAttendanceAdjustmentRequest
{
    [JsonPropertyName("person_id")]
    public string PersonId { get; init; } = "";

    [JsonPropertyName("adjusted_status")]
    public string? AdjustedStatus { get; init; }

    [JsonPropertyName("reason")]
    public string Reason { get; init; } = "";
}

// --- Response DTOs ---

public sealed record SessionResponse
{
    [JsonPropertyName("session_id")]
    public required string SessionId { get; init; }

    [JsonPropertyName("tenant_id")]
    public required string TenantId { get; init; }

    [JsonPropertyName("session_type")]
    public required string SessionType { get; init; }

    [JsonPropertyName("title")]
    public required string Title { get; init; }

    [JsonPropertyName("scheduled_date")]
    public required string ScheduledDate { get; init; }

    [JsonPropertyName("scheduled_start_time")]
    public required string ScheduledStartTime { get; init; }

    [JsonPropertyName("roster_person_ids")]
    public required IReadOnlyList<string> RosterPersonIds { get; init; }

    [JsonPropertyName("version")]
    public required int Version { get; init; }

    [JsonPropertyName("updated_at")]
    public required string UpdatedAt { get; init; }

    [JsonPropertyName("updated_by")]
    public required string UpdatedBy { get; init; }

    public static SessionResponse From(Session session) => new()
    {
        SessionId = session.SessionId,
        TenantId = session.TenantId,
        SessionType = session.SessionType.ToWireString(),
        Title = session.Title,
        ScheduledDate = session.ScheduledDate,
        ScheduledStartTime = session.ScheduledStartTime,
        RosterPersonIds = session.RosterPersonIds,
        Version = session.Version,
        UpdatedAt = session.UpdatedAt,
        UpdatedBy = session.UpdatedBy,
    };
}

public sealed record SessionListResponse
{
    [JsonPropertyName("data")]
    public required IReadOnlyList<SessionResponse> Data { get; init; }
}

public sealed record SessionAttendancePolicyResponse
{
    [JsonPropertyName("tenant_id")]
    public required string TenantId { get; init; }

    [JsonPropertyName("session_type")]
    public required string SessionType { get; init; }

    [JsonPropertyName("late_grace_minutes")]
    public required int LateGraceMinutes { get; init; }

    [JsonPropertyName("absent_after_minutes")]
    public required int AbsentAfterMinutes { get; init; }

    [JsonPropertyName("version")]
    public required int Version { get; init; }

    [JsonPropertyName("updated_at")]
    public required string UpdatedAt { get; init; }

    [JsonPropertyName("updated_by")]
    public required string UpdatedBy { get; init; }

    public static SessionAttendancePolicyResponse From(SessionAttendancePolicy policy) => new()
    {
        TenantId = policy.TenantId,
        SessionType = policy.SessionType.ToWireString(),
        LateGraceMinutes = policy.LateGraceMinutes,
        AbsentAfterMinutes = policy.AbsentAfterMinutes,
        Version = policy.Version,
        UpdatedAt = policy.UpdatedAt,
        UpdatedBy = policy.UpdatedBy,
    };
}

public sealed record AttendanceEvidenceResponse
{
    [JsonPropertyName("event_id")]
    public required string EventId { get; init; }

    [JsonPropertyName("occurred_at")]
    public required string OccurredAt { get; init; }

    [JsonPropertyName("source")]
    public required string Source { get; init; }

    [JsonPropertyName("mode")]
    public required string Mode { get; init; }

    [JsonPropertyName("evidence_method")]
    public required string EvidenceMethod { get; init; }

    [JsonPropertyName("evidence_verified")]
    public required bool EvidenceVerified { get; init; }

    [JsonPropertyName("evidence_confidence")]
    public double? EvidenceConfidence { get; init; }

    public static AttendanceEvidenceResponse From(AttendanceEvidenceRecord record) => new()
    {
        EventId = record.EventId,
        OccurredAt = record.OccurredAt,
        Source = record.Source,
        Mode = record.Mode,
        EvidenceMethod = record.EvidenceMethod,
        EvidenceVerified = record.EvidenceVerified,
        EvidenceConfidence = record.EvidenceConfidence,
    };
}

public sealed record AttendanceAdjustmentResponse
{
    [JsonPropertyName("adjustment_id")]
    public required string AdjustmentId { get; init; }

    [JsonPropertyName("session_id")]
    public required string SessionId { get; init; }

    [JsonPropertyName("person_id")]
    public required string PersonId { get; init; }

    [JsonPropertyName("original_status")]
    public required string OriginalStatus { get; init; }

    [JsonPropertyName("adjusted_status")]
    public required string AdjustedStatus { get; init; }

    [JsonPropertyName("reason")]
    public required string Reason { get; init; }

    [JsonPropertyName("adjusted_by_actor_id")]
    public required string AdjustedByActorId { get; init; }

    [JsonPropertyName("adjusted_at")]
    public required string AdjustedAt { get; init; }

    public static AttendanceAdjustmentResponse From(AttendanceAdjustment adjustment) => new()
    {
        AdjustmentId = adjustment.AdjustmentId,
        SessionId = adjustment.SessionId,
        PersonId = adjustment.PersonId,
        OriginalStatus = adjustment.OriginalStatus.ToWireString(),
        AdjustedStatus = adjustment.AdjustedStatus.ToWireString(),
        Reason = adjustment.Reason,
        AdjustedByActorId = adjustment.AdjustedByActorId,
        AdjustedAt = adjustment.AdjustedAt,
    };
}

public sealed record SessionRosterMemberResponse
{
    [JsonPropertyName("person_id")]
    public required string PersonId { get; init; }

    [JsonPropertyName("computed_status")]
    public required string ComputedStatus { get; init; }

    [JsonPropertyName("effective_status")]
    public required string EffectiveStatus { get; init; }

    [JsonPropertyName("grounding_evidence")]
    public required IReadOnlyList<AttendanceEvidenceResponse> GroundingEvidence { get; init; }

    [JsonPropertyName("policy_version")]
    public required int PolicyVersion { get; init; }

    [JsonPropertyName("computed_at")]
    public required string ComputedAt { get; init; }

    [JsonPropertyName("latest_adjustment")]
    public AttendanceAdjustmentResponse? LatestAdjustment { get; init; }

    public static SessionRosterMemberResponse From(SessionRosterMemberView view) => new()
    {
        PersonId = view.PersonId,
        ComputedStatus = view.Classification.Status.ToWireString(),
        EffectiveStatus = view.EffectiveStatus.ToWireString(),
        GroundingEvidence = [.. view.Classification.GroundingEvidence.Select(AttendanceEvidenceResponse.From)],
        PolicyVersion = view.Classification.PolicyVersion,
        ComputedAt = view.Classification.ComputedAt,
        LatestAdjustment = view.LatestAdjustment is null ? null : AttendanceAdjustmentResponse.From(view.LatestAdjustment),
    };
}

public sealed record SessionRosterResponse
{
    [JsonPropertyName("session")]
    public required SessionResponse Session { get; init; }

    [JsonPropertyName("roster")]
    public required IReadOnlyList<SessionRosterMemberResponse> Roster { get; init; }
}

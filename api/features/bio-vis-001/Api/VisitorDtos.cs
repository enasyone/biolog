using System.Text.Json.Serialization;
using Biolog.Features.Visitors.Domain;

namespace Biolog.Features.Visitors.Api;

// --- Request DTOs (wire shape only - the services own all parsing/validation) ---

public sealed record PreregisterVisitorRequest
{
    [JsonPropertyName("person_id")]
    public string? PersonId { get; init; }

    [JsonPropertyName("full_name")]
    public string? FullName { get; init; }

    [JsonPropertyName("contact_email")]
    public string? ContactEmail { get; init; }

    [JsonPropertyName("contact_phone")]
    public string? ContactPhone { get; init; }

    [JsonPropertyName("host_person_id")]
    public string? HostPersonId { get; init; }

    [JsonPropertyName("vehicle_plate")]
    public string? VehiclePlate { get; init; }

    [JsonPropertyName("temporary_credential_requested")]
    public bool TemporaryCredentialRequested { get; init; }

    [JsonPropertyName("expires_at")]
    public string? ExpiresAt { get; init; }
}

public sealed record VersionedVisitorRequest
{
    [JsonPropertyName("version")]
    public int? Version { get; init; }
}

public sealed record DenyVisitorRequest
{
    [JsonPropertyName("version")]
    public int? Version { get; init; }

    [JsonPropertyName("reason")]
    public string? Reason { get; init; }
}

public sealed record RecordWatchlistResultRequest
{
    [JsonPropertyName("version")]
    public int? Version { get; init; }

    [JsonPropertyName("result")]
    public string? Result { get; init; }
}

public sealed record SetVisitorConfigRequest
{
    [JsonPropertyName("require_vehicle_plate")]
    public bool RequireVehiclePlate { get; init; }

    [JsonPropertyName("require_nda")]
    public bool RequireNda { get; init; }

    [JsonPropertyName("require_safety_briefing")]
    public bool RequireSafetyBriefing { get; init; }

    [JsonPropertyName("require_watchlist_screening")]
    public bool RequireWatchlistScreening { get; init; }

    [JsonPropertyName("allow_temporary_credential")]
    public bool AllowTemporaryCredential { get; init; }

    [JsonPropertyName("default_visit_duration_hours")]
    public int DefaultVisitDurationHours { get; init; }

    [JsonPropertyName("max_visit_duration_hours")]
    public int MaxVisitDurationHours { get; init; }
}

// --- Response DTOs ---

public sealed record VisitorRecordResponse
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("tenant_id")]
    public required string TenantId { get; init; }

    [JsonPropertyName("person_id")]
    public string? PersonId { get; init; }

    [JsonPropertyName("full_name")]
    public required string FullName { get; init; }

    [JsonPropertyName("contact_email")]
    public string? ContactEmail { get; init; }

    [JsonPropertyName("contact_phone")]
    public string? ContactPhone { get; init; }

    [JsonPropertyName("host_person_id")]
    public required string HostPersonId { get; init; }

    [JsonPropertyName("vehicle_plate")]
    public string? VehiclePlate { get; init; }

    [JsonPropertyName("nda_accepted")]
    public required bool NdaAccepted { get; init; }

    [JsonPropertyName("safety_briefing_completed")]
    public required bool SafetyBriefingCompleted { get; init; }

    [JsonPropertyName("watchlist_screening_result")]
    public required string WatchlistScreeningResult { get; init; }

    [JsonPropertyName("temporary_credential_requested")]
    public required bool TemporaryCredentialRequested { get; init; }

    [JsonPropertyName("temporary_credential_issued")]
    public required bool TemporaryCredentialIssued { get; init; }

    [JsonPropertyName("host_approval_status")]
    public required string HostApprovalStatus { get; init; }

    [JsonPropertyName("approval_reason")]
    public string? ApprovalReason { get; init; }

    [JsonPropertyName("visit_status")]
    public required string VisitStatus { get; init; }

    [JsonPropertyName("effective_status")]
    public required string EffectiveStatus { get; init; }

    [JsonPropertyName("expires_at")]
    public required string ExpiresAt { get; init; }

    [JsonPropertyName("created_at")]
    public required string CreatedAt { get; init; }

    [JsonPropertyName("version")]
    public required int Version { get; init; }

    public static VisitorRecordResponse From(VisitorRecord r, string now) => new()
    {
        Id = r.Id,
        TenantId = r.TenantId,
        PersonId = r.PersonId,
        FullName = r.FullName,
        ContactEmail = r.ContactEmail,
        ContactPhone = r.ContactPhone,
        HostPersonId = r.HostPersonId,
        VehiclePlate = r.VehiclePlate,
        NdaAccepted = r.NdaAccepted,
        SafetyBriefingCompleted = r.SafetyBriefingCompleted,
        WatchlistScreeningResult = r.WatchlistScreeningResult.ToWireString(),
        TemporaryCredentialRequested = r.TemporaryCredentialRequested,
        TemporaryCredentialIssued = r.TemporaryCredentialIssued,
        HostApprovalStatus = r.HostApprovalStatus.ToWireString(),
        ApprovalReason = r.ApprovalReason,
        VisitStatus = r.VisitStatus.ToWireString(),
        EffectiveStatus = VisitorStatusEvaluator.EffectiveStatus(r, now).ToWireString(),
        ExpiresAt = r.ExpiresAt,
        CreatedAt = r.CreatedAt,
        Version = r.Version,
    };
}

public sealed record VisitorRecordListResponse
{
    [JsonPropertyName("data")]
    public required IReadOnlyList<VisitorRecordResponse> Data { get; init; }
}

public sealed record VisitorConfigResponse
{
    [JsonPropertyName("tenant_id")]
    public required string TenantId { get; init; }

    [JsonPropertyName("require_vehicle_plate")]
    public required bool RequireVehiclePlate { get; init; }

    [JsonPropertyName("require_nda")]
    public required bool RequireNda { get; init; }

    [JsonPropertyName("require_safety_briefing")]
    public required bool RequireSafetyBriefing { get; init; }

    [JsonPropertyName("require_watchlist_screening")]
    public required bool RequireWatchlistScreening { get; init; }

    [JsonPropertyName("allow_temporary_credential")]
    public required bool AllowTemporaryCredential { get; init; }

    [JsonPropertyName("default_visit_duration_hours")]
    public required int DefaultVisitDurationHours { get; init; }

    [JsonPropertyName("max_visit_duration_hours")]
    public required int MaxVisitDurationHours { get; init; }

    [JsonPropertyName("version")]
    public required int Version { get; init; }

    [JsonPropertyName("updated_at")]
    public required string UpdatedAt { get; init; }

    [JsonPropertyName("updated_by")]
    public required string UpdatedBy { get; init; }

    public static VisitorConfigResponse From(VisitorTenantConfig c) => new()
    {
        TenantId = c.TenantId,
        RequireVehiclePlate = c.RequireVehiclePlate,
        RequireNda = c.RequireNda,
        RequireSafetyBriefing = c.RequireSafetyBriefing,
        RequireWatchlistScreening = c.RequireWatchlistScreening,
        AllowTemporaryCredential = c.AllowTemporaryCredential,
        DefaultVisitDurationHours = c.DefaultVisitDurationHours,
        MaxVisitDurationHours = c.MaxVisitDurationHours,
        Version = c.Version,
        UpdatedAt = c.UpdatedAt,
        UpdatedBy = c.UpdatedBy,
    };
}

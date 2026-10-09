using System.Text.Json.Serialization;
using Biolog.Features.Rbac.Domain;

namespace Biolog.Features.Rbac.Api;

public sealed record SetSensitiveActionRuleRequest
{
    [JsonPropertyName("requires_second_approval")]
    public bool? RequiresSecondApproval { get; init; }
}

public sealed record SensitiveActionRuleResponse
{
    [JsonPropertyName("action_key")]
    public required string ActionKey { get; init; }

    [JsonPropertyName("requires_second_approval")]
    public required bool RequiresSecondApproval { get; init; }

    [JsonPropertyName("is_default")]
    public required bool IsDefault { get; init; }

    [JsonPropertyName("version")]
    public int? Version { get; init; }

    [JsonPropertyName("created_at")]
    public string? CreatedAt { get; init; }

    [JsonPropertyName("updated_at")]
    public string? UpdatedAt { get; init; }

    public static SensitiveActionRuleResponse From(string actionKey, SensitiveActionRule? rule) => new()
    {
        ActionKey = actionKey,
        RequiresSecondApproval = rule?.RequiresSecondApproval ?? false,
        IsDefault = rule is null,
        Version = rule?.Version,
        CreatedAt = rule?.CreatedAt,
        UpdatedAt = rule?.UpdatedAt,
    };
}

public sealed record RequestApprovalRequest
{
    [JsonPropertyName("target_ref")]
    public string? TargetRef { get; init; }

    [JsonPropertyName("reason")]
    public string? Reason { get; init; }
}

public sealed record DecideApprovalRequest
{
    [JsonPropertyName("decision")]
    public string? Decision { get; init; }

    [JsonPropertyName("reason_code")]
    public string? ReasonCode { get; init; }

    [JsonPropertyName("version")]
    public int? Version { get; init; }
}

public sealed record ApprovalRequestResponse
{
    [JsonPropertyName("tenant_id")]
    public required string TenantId { get; init; }

    [JsonPropertyName("action_key")]
    public required string ActionKey { get; init; }

    [JsonPropertyName("target_ref")]
    public required string TargetRef { get; init; }

    [JsonPropertyName("sequence_number")]
    public required int SequenceNumber { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("requested_by_actor_id")]
    public required string RequestedByActorId { get; init; }

    [JsonPropertyName("request_reason")]
    public required string RequestReason { get; init; }

    [JsonPropertyName("decided_by_actor_id")]
    public string? DecidedByActorId { get; init; }

    [JsonPropertyName("decision_reason_code")]
    public string? DecisionReasonCode { get; init; }

    [JsonPropertyName("decided_at")]
    public string? DecidedAt { get; init; }

    [JsonPropertyName("version")]
    public required int Version { get; init; }

    [JsonPropertyName("created_at")]
    public required string CreatedAt { get; init; }

    [JsonPropertyName("updated_at")]
    public required string UpdatedAt { get; init; }

    public static ApprovalRequestResponse From(ApprovalRequest request) => new()
    {
        TenantId = request.TenantId,
        ActionKey = request.ActionKey,
        TargetRef = request.TargetRef,
        SequenceNumber = request.SequenceNumber,
        Status = request.Status.ToWireString(),
        RequestedByActorId = request.RequestedByActorId,
        RequestReason = request.RequestReason,
        DecidedByActorId = request.DecidedByActorId,
        DecisionReasonCode = request.DecisionReasonCode,
        DecidedAt = request.DecidedAt,
        Version = request.Version,
        CreatedAt = request.CreatedAt,
        UpdatedAt = request.UpdatedAt,
    };
}

public sealed record ApprovalRequestListResponse
{
    [JsonPropertyName("data")]
    public required IReadOnlyList<ApprovalRequestResponse> Data { get; init; }
}

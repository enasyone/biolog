using System.Text.Json.Serialization;
using Biolog.Features.AccessPolicy.Domain;

namespace Biolog.Features.AccessPolicy.Api;

// --- Request DTOs (wire shape only - AccessPolicyService owns all parsing/validation) ---

public sealed record SubjectMatchRequest
{
    [JsonPropertyName("kind")]
    public string? Kind { get; init; }

    [JsonPropertyName("value")]
    public string? Value { get; init; }
}

public sealed record CredentialClassMatchRequest
{
    [JsonPropertyName("kind")]
    public string? Kind { get; init; }

    [JsonPropertyName("credential_class")]
    public string? CredentialClass { get; init; }
}

public sealed record LocationMatchRequest
{
    [JsonPropertyName("kind")]
    public string? Kind { get; init; }

    [JsonPropertyName("location_id")]
    public string? LocationId { get; init; }
}

public sealed record ScheduleWindowRequest
{
    [JsonPropertyName("day_of_week")]
    public string? DayOfWeek { get; init; }

    [JsonPropertyName("start_time")]
    public string? StartTime { get; init; }

    [JsonPropertyName("end_time")]
    public string? EndTime { get; init; }
}

public sealed record ScheduleMatchRequest
{
    [JsonPropertyName("kind")]
    public string? Kind { get; init; }

    [JsonPropertyName("windows")]
    public IReadOnlyList<ScheduleWindowRequest>? Windows { get; init; }
}

public sealed record IdentityStatusMatchRequest
{
    [JsonPropertyName("any_status")]
    public bool? AnyStatus { get; init; }

    [JsonPropertyName("allowed_statuses")]
    public IReadOnlyList<string>? AllowedStatuses { get; init; }
}

public sealed record CardStatusMatchRequest
{
    [JsonPropertyName("any_status")]
    public bool? AnyStatus { get; init; }

    [JsonPropertyName("allowed_statuses")]
    public IReadOnlyList<string>? AllowedStatuses { get; init; }
}

public sealed record PolicyRuleRequest
{
    [JsonPropertyName("rule_id")]
    public int? RuleId { get; init; }

    [JsonPropertyName("effect")]
    public string? Effect { get; init; }

    [JsonPropertyName("subject")]
    public SubjectMatchRequest? Subject { get; init; }

    [JsonPropertyName("credential")]
    public CredentialClassMatchRequest? Credential { get; init; }

    [JsonPropertyName("location")]
    public LocationMatchRequest? Location { get; init; }

    [JsonPropertyName("schedule")]
    public ScheduleMatchRequest? Schedule { get; init; }

    [JsonPropertyName("identity_status")]
    public IdentityStatusMatchRequest? IdentityStatus { get; init; }

    [JsonPropertyName("card_status")]
    public CardStatusMatchRequest? CardStatus { get; init; }

    [JsonPropertyName("required_assurance_level")]
    public string? RequiredAssuranceLevel { get; init; }
}

public sealed record CreateAccessPolicyRequest
{
    [JsonPropertyName("policy_key")]
    public string? PolicyKey { get; init; }

    [JsonPropertyName("rules")]
    public IReadOnlyList<PolicyRuleRequest>? Rules { get; init; }
}

public sealed record ReviseDraftRequest
{
    [JsonPropertyName("version")]
    public int? Version { get; init; }

    [JsonPropertyName("rules")]
    public IReadOnlyList<PolicyRuleRequest>? Rules { get; init; }
}

public sealed record PublishAccessPolicyRequest
{
    [JsonPropertyName("version")]
    public int? Version { get; init; }
}

public sealed record EvaluateAccessPolicyRequest
{
    [JsonPropertyName("person_id")]
    public string? PersonId { get; init; }

    [JsonPropertyName("profile_key")]
    public string? ProfileKey { get; init; }

    [JsonPropertyName("credential_class")]
    public string? CredentialClass { get; init; }

    [JsonPropertyName("location_id")]
    public string? LocationId { get; init; }

    [JsonPropertyName("occurred_at")]
    public string? OccurredAt { get; init; }

    [JsonPropertyName("presented_card_status")]
    public string? PresentedCardStatus { get; init; }

    [JsonPropertyName("presented_assurance_level")]
    public string? PresentedAssuranceLevel { get; init; }
}

// --- Response DTOs ---

public sealed record PolicyRuleResponse
{
    [JsonPropertyName("rule_id")]
    public required int RuleId { get; init; }

    [JsonPropertyName("effect")]
    public required string Effect { get; init; }

    [JsonPropertyName("subject")]
    public required SubjectMatchRequest Subject { get; init; }

    [JsonPropertyName("credential")]
    public required CredentialClassMatchRequest Credential { get; init; }

    [JsonPropertyName("location")]
    public required LocationMatchRequest Location { get; init; }

    [JsonPropertyName("schedule")]
    public required ScheduleMatchRequest Schedule { get; init; }

    [JsonPropertyName("identity_status")]
    public required IdentityStatusMatchRequest IdentityStatus { get; init; }

    [JsonPropertyName("card_status")]
    public required CardStatusMatchRequest CardStatus { get; init; }

    [JsonPropertyName("required_assurance_level")]
    public required string RequiredAssuranceLevel { get; init; }

    public static PolicyRuleResponse From(PolicyRule rule) => new()
    {
        RuleId = rule.RuleId,
        Effect = rule.Effect.ToWireString(),
        Subject = new SubjectMatchRequest { Kind = rule.Subject.Kind.ToWireString(), Value = rule.Subject.Value },
        Credential = new CredentialClassMatchRequest { Kind = rule.Credential.Kind.ToWireString(), CredentialClass = rule.Credential.CredentialClass },
        Location = new LocationMatchRequest { Kind = rule.Location.Kind.ToWireString(), LocationId = rule.Location.LocationId },
        Schedule = new ScheduleMatchRequest
        {
            Kind = rule.Schedule.Kind.ToWireString(),
            Windows = [.. rule.Schedule.Windows.Select(w => new ScheduleWindowRequest
            {
                DayOfWeek = w.DayOfWeek.ToString().ToUpperInvariant(), StartTime = w.StartTime.ToString("HH:mm"), EndTime = w.EndTime.ToString("HH:mm"),
            })],
        },
        IdentityStatus = new IdentityStatusMatchRequest
        {
            AnyStatus = rule.IdentityStatus.AnyStatus, AllowedStatuses = [.. rule.IdentityStatus.AllowedStatuses.Select(s => s.ToWireString())],
        },
        CardStatus = new CardStatusMatchRequest
        {
            AnyStatus = rule.CardStatus.AnyStatus, AllowedStatuses = [.. rule.CardStatus.AllowedStatuses.Select(s => s.ToWireString())],
        },
        RequiredAssuranceLevel = rule.RequiredAssuranceLevel.ToWireString(),
    };
}

public sealed record AccessPolicyVersionResponse
{
    [JsonPropertyName("version_number")]
    public required int VersionNumber { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("author_actor_id")]
    public required string AuthorActorId { get; init; }

    [JsonPropertyName("published_by_actor_id")]
    public string? PublishedByActorId { get; init; }

    [JsonPropertyName("created_at")]
    public required string CreatedAt { get; init; }

    [JsonPropertyName("published_at")]
    public string? PublishedAt { get; init; }

    [JsonPropertyName("rules")]
    public required IReadOnlyList<PolicyRuleResponse> Rules { get; init; }

    public static AccessPolicyVersionResponse From(AccessPolicyVersionContent version) => new()
    {
        VersionNumber = version.VersionNumber,
        Status = version.Status.ToWireString(),
        AuthorActorId = version.AuthorActorId,
        PublishedByActorId = version.PublishedByActorId,
        CreatedAt = version.CreatedAt,
        PublishedAt = version.PublishedAt,
        Rules = [.. version.Rules.Select(PolicyRuleResponse.From)],
    };
}

public sealed record AccessPolicyResponse
{
    [JsonPropertyName("tenant_id")]
    public required string TenantId { get; init; }

    [JsonPropertyName("policy_key")]
    public required string PolicyKey { get; init; }

    [JsonPropertyName("concurrency_version")]
    public required int ConcurrencyVersion { get; init; }

    [JsonPropertyName("published_version_number")]
    public int? PublishedVersionNumber { get; init; }

    [JsonPropertyName("created_at")]
    public required string CreatedAt { get; init; }

    [JsonPropertyName("updated_at")]
    public required string UpdatedAt { get; init; }

    [JsonPropertyName("latest_version")]
    public required AccessPolicyVersionResponse LatestVersion { get; init; }

    public static AccessPolicyResponse From(Domain.AccessPolicy policy) => new()
    {
        TenantId = policy.TenantId,
        PolicyKey = policy.PolicyKey,
        ConcurrencyVersion = policy.ConcurrencyVersion,
        PublishedVersionNumber = policy.PublishedVersionNumber,
        CreatedAt = policy.CreatedAt,
        UpdatedAt = policy.UpdatedAt,
        LatestVersion = AccessPolicyVersionResponse.From(policy.Latest),
    };
}

public sealed record AccessPolicyListResponse
{
    [JsonPropertyName("data")]
    public required IReadOnlyList<AccessPolicyResponse> Data { get; init; }
}

public sealed record EvaluateAccessPolicyResponse
{
    [JsonPropertyName("decision")]
    public required string Decision { get; init; }

    [JsonPropertyName("reason_code")]
    public required string ReasonCode { get; init; }

    [JsonPropertyName("policy_key")]
    public required string PolicyKey { get; init; }

    [JsonPropertyName("policy_version_number")]
    public int? PolicyVersionNumber { get; init; }

    [JsonPropertyName("matched_rule_id")]
    public int? MatchedRuleId { get; init; }

    public static EvaluateAccessPolicyResponse From(PolicyEvaluationResult result) => new()
    {
        Decision = result.Decision.ToWireString(),
        ReasonCode = result.ReasonCode,
        PolicyKey = result.PolicyKey,
        PolicyVersionNumber = result.PolicyVersionNumber,
        MatchedRuleId = result.MatchedRuleId,
    };
}

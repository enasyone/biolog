using System.Text.Json.Serialization;
using Biolog.Features.AccessDecisions.Domain;

namespace Biolog.Features.AccessDecisions.Api;

// --- Request DTOs (wire shape only - AccessDecisionService owns all parsing/validation) ---

public sealed record BiometricSampleRequest
{
    [JsonPropertyName("payload_base64")]
    public string? PayloadBase64 { get; init; }

    [JsonPropertyName("content_type")]
    public string? ContentType { get; init; }

    [JsonPropertyName("captured_at")]
    public string? CapturedAt { get; init; }
}

/// <summary>
/// AC01: <see cref="FingerprintSample"/>/<see cref="FaceSample"/> are BOTH optional - a reader/gate
/// presenting only a card (no biometric factor) is a legitimate Basic-assurance request; the
/// policy engine, not this endpoint, decides whether Basic is sufficient. When present, either
/// sample is evidence only, never itself the decision (see <see cref="AccessDecisionCombiner"/>).
/// </summary>
public sealed record DecideAccessRequest
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

    [JsonPropertyName("policy_key")]
    public string? PolicyKey { get; init; }

    [JsonPropertyName("fingerprint_sample")]
    public BiometricSampleRequest? FingerprintSample { get; init; }

    [JsonPropertyName("face_sample")]
    public BiometricSampleRequest? FaceSample { get; init; }

    [JsonPropertyName("idempotency_key")]
    public string? IdempotencyKey { get; init; }
}

// --- Response DTOs ---

public sealed record BiometricEvidenceEntryResponse
{
    [JsonPropertyName("modality")]
    public required string Modality { get; init; }

    [JsonPropertyName("comparison_outcome")]
    public required string ComparisonOutcome { get; init; }

    [JsonPropertyName("score")]
    public double? Score { get; init; }

    public static BiometricEvidenceEntryResponse From(BiometricEvidenceEntry entry) => new()
    {
        Modality = entry.Modality.ToWireString(),
        ComparisonOutcome = entry.ComparisonOutcome.ToWireString(),
        Score = entry.Score,
    };
}

public sealed record AccessDecisionResponse
{
    [JsonPropertyName("event_id")]
    public required string EventId { get; init; }

    [JsonPropertyName("tenant_id")]
    public required string TenantId { get; init; }

    [JsonPropertyName("person_id")]
    public required string PersonId { get; init; }

    [JsonPropertyName("profile_key")]
    public string? ProfileKey { get; init; }

    [JsonPropertyName("credential_class")]
    public required string CredentialClass { get; init; }

    [JsonPropertyName("location_id")]
    public required string LocationId { get; init; }

    [JsonPropertyName("presented_card_status")]
    public required string PresentedCardStatus { get; init; }

    [JsonPropertyName("presented_assurance_level")]
    public required string PresentedAssuranceLevel { get; init; }

    [JsonPropertyName("biometric_evidence")]
    public required IReadOnlyList<BiometricEvidenceEntryResponse> BiometricEvidence { get; init; }

    [JsonPropertyName("policy_key")]
    public required string PolicyKey { get; init; }

    [JsonPropertyName("decision")]
    public required string Decision { get; init; }

    [JsonPropertyName("reason_code")]
    public required string ReasonCode { get; init; }

    [JsonPropertyName("policy_version_number")]
    public int? PolicyVersionNumber { get; init; }

    [JsonPropertyName("matched_rule_id")]
    public int? MatchedRuleId { get; init; }

    [JsonPropertyName("occurred_at")]
    public required string OccurredAt { get; init; }

    [JsonPropertyName("recorded_at")]
    public required string RecordedAt { get; init; }

    [JsonPropertyName("correlation_id")]
    public required string CorrelationId { get; init; }

    [JsonPropertyName("idempotency_key")]
    public string? IdempotencyKey { get; init; }

    public static AccessDecisionResponse From(AccessDecisionEvent e) => new()
    {
        EventId = e.EventId,
        TenantId = e.TenantId,
        PersonId = e.PersonId,
        ProfileKey = e.ProfileKey,
        CredentialClass = e.CredentialClass,
        LocationId = e.LocationId,
        PresentedCardStatus = e.PresentedCardStatus.ToWireString(),
        PresentedAssuranceLevel = e.PresentedAssuranceLevel.ToWireString(),
        BiometricEvidence = [.. e.BiometricEvidence.Select(BiometricEvidenceEntryResponse.From)],
        PolicyKey = e.PolicyKey,
        Decision = e.Decision.ToWireString(),
        ReasonCode = e.ReasonCode,
        PolicyVersionNumber = e.PolicyVersionNumber,
        MatchedRuleId = e.MatchedRuleId,
        OccurredAt = e.OccurredAt,
        RecordedAt = e.RecordedAt,
        CorrelationId = e.CorrelationId,
        IdempotencyKey = e.IdempotencyKey,
    };
}

public sealed record AccessDecisionListResponse
{
    [JsonPropertyName("data")]
    public required IReadOnlyList<AccessDecisionResponse> Data { get; init; }
}

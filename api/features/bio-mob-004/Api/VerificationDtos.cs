using System.Text.Json.Serialization;
using Biolog.Features.MobileVerification.Domain;

namespace Biolog.Features.MobileVerification.Api;

// --- Request DTOs ---

public sealed record BiometricSampleRequest
{
    [JsonPropertyName("payload_base64")]
    public string? PayloadBase64 { get; init; }

    [JsonPropertyName("content_type")]
    public string? ContentType { get; init; }

    [JsonPropertyName("captured_at")]
    public string? CapturedAt { get; init; }
}

/// <summary>AC01: <see cref="FingerprintSample"/>/<see cref="FaceSample"/> are both optional - a card/NFC/QR-only presentation (no biometric factor) is a legitimate request; the downstream policy engine, not this endpoint, decides whether that is sufficient. The idempotency key travels as the standard <c>Idempotency-Key</c> header (ADR-0001 §3.4), not a body field.</summary>
public sealed record RequestVerificationDecisionRequest
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
}

public sealed record SetDeviceRoleRequest
{
    [JsonPropertyName("role")]
    public string? Role { get; init; }
}

/// <summary>AC03: Supervisor-only. <see cref="PersonId"/> travels explicitly rather than requiring a lookup back into BIO-ACC-002's own event store - the officer's screen already has it from the decision just displayed.</summary>
public sealed record OverrideDecisionRequest
{
    [JsonPropertyName("person_id")]
    public string? PersonId { get; init; }

    [JsonPropertyName("reason_code")]
    public string? ReasonCode { get; init; }
}

// --- Response DTOs ---

/// <summary>AC01: deliberately mirrors <see cref="VerificationDecisionResult"/> field-for-field - no biometric evidence/score field exists on this type at all.</summary>
public sealed record VerificationDecisionResponse
{
    [JsonPropertyName("event_id")]
    public required string EventId { get; init; }

    [JsonPropertyName("person_id")]
    public required string PersonId { get; init; }

    [JsonPropertyName("decision")]
    public required string Decision { get; init; }

    [JsonPropertyName("reason_code")]
    public required string ReasonCode { get; init; }

    [JsonPropertyName("presented_assurance_level")]
    public required string PresentedAssuranceLevel { get; init; }

    [JsonPropertyName("occurred_at")]
    public required string OccurredAt { get; init; }

    [JsonPropertyName("recorded_at")]
    public required string RecordedAt { get; init; }

    [JsonPropertyName("correlation_id")]
    public required string CorrelationId { get; init; }

    [JsonPropertyName("idempotency_key")]
    public string? IdempotencyKey { get; init; }

    public static VerificationDecisionResponse From(VerificationDecisionResult result) => new()
    {
        EventId = result.EventId,
        PersonId = result.PersonId,
        Decision = result.Decision,
        ReasonCode = result.ReasonCode,
        PresentedAssuranceLevel = result.PresentedAssuranceLevel,
        OccurredAt = result.OccurredAt,
        RecordedAt = result.RecordedAt,
        CorrelationId = result.CorrelationId,
        IdempotencyKey = result.IdempotencyKey,
    };
}

public sealed record VerificationDecisionListResponse
{
    [JsonPropertyName("data")]
    public required IReadOnlyList<VerificationDecisionResponse> Data { get; init; }
}

public sealed record DeviceRoleResponse
{
    [JsonPropertyName("device_id")]
    public required string DeviceId { get; init; }

    [JsonPropertyName("role")]
    public required string Role { get; init; }

    public static DeviceRoleResponse From(string deviceId, VerificationDeviceRole role) => new()
    {
        DeviceId = deviceId,
        Role = role.ToWireString(),
    };
}

public sealed record VerificationOverrideResponse
{
    [JsonPropertyName("override_id")]
    public required string OverrideId { get; init; }

    [JsonPropertyName("decision_event_id")]
    public required string DecisionEventId { get; init; }

    [JsonPropertyName("person_id")]
    public required string PersonId { get; init; }

    [JsonPropertyName("reason_code")]
    public required string ReasonCode { get; init; }

    [JsonPropertyName("overridden_at")]
    public required string OverriddenAt { get; init; }

    public static VerificationOverrideResponse From(VerificationOverride entry) => new()
    {
        OverrideId = entry.Id,
        DecisionEventId = entry.DecisionEventId,
        PersonId = entry.PersonId,
        ReasonCode = entry.ReasonCode,
        OverriddenAt = entry.OverriddenAt,
    };
}

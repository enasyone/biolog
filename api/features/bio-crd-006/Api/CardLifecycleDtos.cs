using System.Text.Json;
using System.Text.Json.Serialization;
using Biolog.Features.CardLifecycle.Audit;
using Biolog.Features.CardLifecycle.Domain;

namespace Biolog.Features.CardLifecycle.Api;

public static class CardLifecycleJsonOptions
{
    public static readonly JsonSerializerOptions Strict = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
}

/// <summary>BIO-CRD-006-AC01: "configurable reason."</summary>
public sealed record ReportCardRequest
{
    [JsonPropertyName("reason_code")]
    public required string ReasonCode { get; init; }

    [JsonPropertyName("reason")]
    public string? Reason { get; init; }

    [JsonPropertyName("expected_version")]
    public int? ExpectedVersion { get; init; }
}

/// <summary>Every mutating request other than the first-ever report takes only the CAS precondition (ADR-0001 §3.2).</summary>
public sealed record CardLifecycleStepRequest
{
    [JsonPropertyName("expected_version")]
    public int? ExpectedVersion { get; init; }
}

public sealed record ReplacementCardWireRequest
{
    [JsonPropertyName("template_ref")]
    public required string TemplateRef { get; init; }

    [JsonPropertyName("photo_ref")]
    public string? PhotoRef { get; init; }

    [JsonPropertyName("logo_ref")]
    public string? LogoRef { get; init; }

    [JsonPropertyName("signature_ref")]
    public string? SignatureRef { get; init; }

    [JsonPropertyName("printer_ref")]
    public required string PrinterRef { get; init; }
}

public sealed record CardLifecycleRecordResponse
{
    [JsonPropertyName("person_id")]
    public required string PersonId { get; init; }

    [JsonPropertyName("card_class")]
    public required string CardClass { get; init; }

    [JsonPropertyName("sequence_number")]
    public required int SequenceNumber { get; init; }

    [JsonPropertyName("lock_state")]
    public required string LockState { get; init; }

    [JsonPropertyName("version")]
    public required int Version { get; init; }

    [JsonPropertyName("reason_code")]
    public required string ReasonCode { get; init; }

    [JsonPropertyName("reason")]
    public string? Reason { get; init; }

    [JsonPropertyName("reported_at")]
    public required string ReportedAt { get; init; }

    [JsonPropertyName("reported_by_actor_id")]
    public required string ReportedByActorId { get; init; }

    [JsonPropertyName("recovered_at")]
    public string? RecoveredAt { get; init; }

    [JsonPropertyName("recovered_by_actor_id")]
    public string? RecoveredByActorId { get; init; }

    [JsonPropertyName("revoked_at")]
    public string? RevokedAt { get; init; }

    [JsonPropertyName("revoked_by_actor_id")]
    public string? RevokedByActorId { get; init; }

    [JsonPropertyName("revoked_reason_code")]
    public string? RevokedReasonCode { get; init; }

    [JsonPropertyName("created_at")]
    public required string CreatedAt { get; init; }

    [JsonPropertyName("updated_at")]
    public required string UpdatedAt { get; init; }

    public static CardLifecycleRecordResponse From(CardLifecycleRecord record) => new()
    {
        PersonId = record.PersonId,
        CardClass = record.CardClass,
        SequenceNumber = record.SequenceNumber,
        LockState = record.LockState.ToWireString(),
        Version = record.Version,
        ReasonCode = record.ReasonCode,
        Reason = record.Reason,
        ReportedAt = record.ReportedAt,
        ReportedByActorId = record.ReportedBy.Id,
        RecoveredAt = record.RecoveredAt,
        RecoveredByActorId = record.RecoveredBy?.Id,
        RevokedAt = record.RevokedAt,
        RevokedByActorId = record.RevokedBy?.Id,
        RevokedReasonCode = record.RevokedReasonCode,
        CreatedAt = record.CreatedAt,
        UpdatedAt = record.UpdatedAt,
    };
}

public sealed record CardLifecycleDenyListResponse
{
    [JsonPropertyName("data")]
    public required IReadOnlyList<CardLifecycleRecordResponse> Data { get; init; }
}

public sealed record CardIssueSnapshotResponse
{
    [JsonPropertyName("person_id")]
    public required string PersonId { get; init; }

    [JsonPropertyName("card_class")]
    public required string CardClass { get; init; }

    [JsonPropertyName("sequence_number")]
    public required int SequenceNumber { get; init; }

    [JsonPropertyName("sequence_display")]
    public required string SequenceDisplay { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("version")]
    public required int Version { get; init; }

    public static CardIssueSnapshotResponse From(CardIssueSnapshot snapshot) => new()
    {
        PersonId = snapshot.PersonId,
        CardClass = snapshot.CardClass,
        SequenceNumber = snapshot.SequenceNumber,
        SequenceDisplay = snapshot.SequenceNumber.ToString("D3"),
        Status = snapshot.Status.ToString().ToLowerInvariant(),
        Version = snapshot.Version,
    };
}

public sealed record PresentationCheckRequest
{
    [JsonPropertyName("device_ref")]
    public string? DeviceRef { get; init; }
}

public sealed record PresentationCheckResponse
{
    [JsonPropertyName("outcome")]
    public required string Outcome { get; init; }

    [JsonPropertyName("reason_code")]
    public required string ReasonCode { get; init; }

    [JsonPropertyName("event_id")]
    public required string EventId { get; init; }

    public static PresentationCheckResponse From(PresentationCheckResult result) => new()
    {
        Outcome = result.Outcome.ToWireString(),
        ReasonCode = result.ReasonCode,
        EventId = result.EventId,
    };
}

/// <summary>AC05's offline sync intake payload - a reader submits what it already decided locally, unnested from any person path since it is a batch-style tenant-level intake surface.</summary>
public sealed record PresentationEventIntakeRequest
{
    [JsonPropertyName("person_id")]
    public required string PersonId { get; init; }

    [JsonPropertyName("card_class")]
    public required string CardClass { get; init; }

    [JsonPropertyName("sequence_number")]
    public required int SequenceNumber { get; init; }

    [JsonPropertyName("outcome")]
    public required string Outcome { get; init; }

    [JsonPropertyName("reason_code")]
    public required string ReasonCode { get; init; }

    [JsonPropertyName("occurred_at")]
    public required string OccurredAt { get; init; }

    [JsonPropertyName("device_ref")]
    public string? DeviceRef { get; init; }
}

public sealed record PresentationEventResponse
{
    [JsonPropertyName("event_id")]
    public required string EventId { get; init; }

    [JsonPropertyName("person_id")]
    public required string PersonId { get; init; }

    [JsonPropertyName("card_class")]
    public required string CardClass { get; init; }

    [JsonPropertyName("sequence_number")]
    public required int SequenceNumber { get; init; }

    [JsonPropertyName("outcome")]
    public required string Outcome { get; init; }

    [JsonPropertyName("reason_code")]
    public required string ReasonCode { get; init; }

    [JsonPropertyName("device_ref")]
    public string? DeviceRef { get; init; }

    [JsonPropertyName("recorded_by_actor_id")]
    public required string RecordedByActorId { get; init; }

    [JsonPropertyName("recorded_by_actor_type")]
    public required string RecordedByActorType { get; init; }

    [JsonPropertyName("source")]
    public required string Source { get; init; }

    [JsonPropertyName("occurred_at")]
    public required string OccurredAt { get; init; }

    [JsonPropertyName("received_at")]
    public required string ReceivedAt { get; init; }

    public static PresentationEventResponse From(PresentationEvent evt) => new()
    {
        EventId = evt.EventId,
        PersonId = evt.PersonId,
        CardClass = evt.CardClass,
        SequenceNumber = evt.SequenceNumber,
        Outcome = evt.Outcome.ToWireString(),
        ReasonCode = evt.ReasonCode,
        DeviceRef = evt.DeviceRef,
        RecordedByActorId = evt.RecordedBy.Id,
        RecordedByActorType = evt.RecordedBy.Type.ToWireString(),
        Source = evt.Source,
        OccurredAt = evt.OccurredAt,
        ReceivedAt = evt.ReceivedAt,
    };
}

public sealed record RecoveryPolicyRequest
{
    [JsonPropertyName("reason_code")]
    public required string ReasonCode { get; init; }

    [JsonPropertyName("recovery_permitted")]
    public required bool RecoveryPermitted { get; init; }
}

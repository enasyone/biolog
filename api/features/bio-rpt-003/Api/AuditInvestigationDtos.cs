using System.Text.Json.Serialization;
using Biolog.Features.AuditInvestigation.Audit;
using Biolog.Features.AuditInvestigation.Domain;

namespace Biolog.Features.AuditInvestigation.Api;

public sealed record AuditActorDto
{
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("id")]
    public required string Id { get; init; }
}

public sealed record AuditTargetDto
{
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("id")]
    public string? Id { get; init; }
}

public sealed record AuditDeviceRefDto
{
    [JsonPropertyName("device_id")]
    public string? DeviceId { get; init; }

    [JsonPropertyName("workstation")]
    public string? Workstation { get; init; }
}

public sealed record AuditInvestigationRecordResponse
{
    [JsonPropertyName("source_feature")]
    public required string SourceFeature { get; init; }

    [JsonPropertyName("event_id")]
    public required string EventId { get; init; }

    [JsonPropertyName("schema_version")]
    public required int SchemaVersion { get; init; }

    [JsonPropertyName("tenant_id")]
    public required string TenantId { get; init; }

    [JsonPropertyName("actor")]
    public required AuditActorDto Actor { get; init; }

    [JsonPropertyName("device")]
    public AuditDeviceRefDto? Device { get; init; }

    [JsonPropertyName("person_id")]
    public string? PersonId { get; init; }

    [JsonPropertyName("action")]
    public required string Action { get; init; }

    [JsonPropertyName("target")]
    public required AuditTargetDto Target { get; init; }

    [JsonPropertyName("outcome")]
    public required string Outcome { get; init; }

    [JsonPropertyName("reason_code")]
    public required string ReasonCode { get; init; }

    [JsonPropertyName("correlation_id")]
    public required string CorrelationId { get; init; }

    [JsonPropertyName("causation_id")]
    public string? CausationId { get; init; }

    [JsonPropertyName("occurred_at")]
    public required string OccurredAt { get; init; }

    [JsonPropertyName("received_at")]
    public required string ReceivedAt { get; init; }

    [JsonPropertyName("classification")]
    public required string Classification { get; init; }

    [JsonPropertyName("withheld")]
    public required IReadOnlyList<string> Withheld { get; init; }

    [JsonPropertyName("detail")]
    public required IReadOnlyDictionary<string, object?> Detail { get; init; }

    public static AuditInvestigationRecordResponse From(NormalizedAuditRecord record) => new()
    {
        SourceFeature = record.SourceFeature,
        EventId = record.EventId,
        SchemaVersion = record.SchemaVersion,
        TenantId = record.TenantId,
        Actor = new AuditActorDto { Type = record.Actor.Type.ToWireString(), Id = record.Actor.Id },
        Device = record.Device is null ? null : new AuditDeviceRefDto { DeviceId = record.Device.DeviceId, Workstation = record.Device.Workstation },
        PersonId = record.PersonId,
        Action = record.Action,
        Target = new AuditTargetDto { Type = record.Target.Type, Id = record.Target.Id },
        Outcome = record.Outcome.ToWireString(),
        ReasonCode = record.ReasonCode,
        CorrelationId = record.CorrelationId,
        CausationId = record.CausationId,
        OccurredAt = record.OccurredAt,
        ReceivedAt = record.ReceivedAt,
        Classification = record.Classification,
        Withheld = record.Withheld,
        Detail = record.Detail,
    };
}

public sealed record AuditInvestigationSearchResponse
{
    [JsonPropertyName("records")]
    public required IReadOnlyList<AuditInvestigationRecordResponse> Records { get; init; }

    [JsonPropertyName("total_count")]
    public required int TotalCount { get; init; }

    [JsonPropertyName("page")]
    public required int Page { get; init; }

    [JsonPropertyName("page_size")]
    public required int PageSize { get; init; }
}

/// <summary>Body of <c>POST /export</c> - the same filter shape as the search query params, JSON-bound so a filter with several values stays a clean object rather than a long query string.</summary>
public sealed record AuditInvestigationExportRequest
{
    [JsonPropertyName("actor_type")]
    public string? ActorType { get; init; }

    [JsonPropertyName("actor_id")]
    public string? ActorId { get; init; }

    [JsonPropertyName("person_id")]
    public string? PersonId { get; init; }

    [JsonPropertyName("action")]
    public string? Action { get; init; }

    [JsonPropertyName("device_id")]
    public string? DeviceId { get; init; }

    [JsonPropertyName("reason_code")]
    public string? ReasonCode { get; init; }

    [JsonPropertyName("occurred_from")]
    public string? OccurredFrom { get; init; }

    [JsonPropertyName("occurred_to")]
    public string? OccurredTo { get; init; }
}

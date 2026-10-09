using System.Text.Json.Serialization;
using Biolog.Features.Integrations.Domain;

namespace Biolog.Features.Integrations.Api;

public static class ImportJsonOptions
{
    public static readonly System.Text.Json.JsonSerializerOptions Strict = new()
    {
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower,
    };
}

public sealed record FieldMappingRequest
{
    [JsonPropertyName("source_field")]
    public string? SourceField { get; init; }

    [JsonPropertyName("person_field")]
    public string? PersonField { get; init; }
}

public sealed record CreateMappingRequest
{
    [JsonPropertyName("source_system")]
    public string? SourceSystem { get; init; }

    [JsonPropertyName("sync_direction")]
    public string? SyncDirection { get; init; }

    [JsonPropertyName("display_name")]
    public string? DisplayName { get; init; }

    [JsonPropertyName("field_mappings")]
    public IReadOnlyList<FieldMappingRequest>? FieldMappings { get; init; }
}

public sealed record UpdateMappingRequest
{
    [JsonPropertyName("version")]
    public int? Version { get; init; }

    [JsonPropertyName("sync_direction")]
    public string? SyncDirection { get; init; }

    [JsonPropertyName("display_name")]
    public string? DisplayName { get; init; }

    [JsonPropertyName("field_mappings")]
    public IReadOnlyList<FieldMappingRequest>? FieldMappings { get; init; }

    [JsonPropertyName("active")]
    public bool? Active { get; init; }
}

public sealed record ImportRowRequest
{
    [JsonPropertyName("external_key")]
    public string? ExternalKey { get; init; }

    [JsonPropertyName("fields")]
    public IReadOnlyDictionary<string, string?>? Fields { get; init; }
}

public sealed record ImportRunRequest
{
    [JsonPropertyName("rows")]
    public IReadOnlyList<ImportRowRequest>? Rows { get; init; }
}

public sealed record ImportSourceMappingResponse
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("tenant_id")]
    public required string TenantId { get; init; }

    [JsonPropertyName("source_system")]
    public required string SourceSystem { get; init; }

    [JsonPropertyName("sync_direction")]
    public required string SyncDirection { get; init; }

    [JsonPropertyName("display_name")]
    public required string DisplayName { get; init; }

    [JsonPropertyName("field_mappings")]
    public required IReadOnlyList<FieldMappingRequest> FieldMappings { get; init; }

    [JsonPropertyName("active")]
    public required bool Active { get; init; }

    [JsonPropertyName("version")]
    public required int Version { get; init; }

    [JsonPropertyName("created_at")]
    public required string CreatedAt { get; init; }

    [JsonPropertyName("updated_at")]
    public required string UpdatedAt { get; init; }

    public static ImportSourceMappingResponse From(ImportSourceMapping mapping) => new()
    {
        Id = mapping.Id,
        TenantId = mapping.TenantId,
        SourceSystem = mapping.SourceSystem.ToWireString(),
        SyncDirection = mapping.SyncDirection.ToWireString(),
        DisplayName = mapping.DisplayName,
        FieldMappings = [.. mapping.FieldMappings.Select(m => new FieldMappingRequest { SourceField = m.SourceField, PersonField = m.PersonField })],
        Active = mapping.Active,
        Version = mapping.Version,
        CreatedAt = mapping.CreatedAt,
        UpdatedAt = mapping.UpdatedAt,
    };
}

public sealed record FieldConflictResponse
{
    [JsonPropertyName("person_field")]
    public required string PersonField { get; init; }

    [JsonPropertyName("existing_value")]
    public string? ExistingValue { get; init; }

    [JsonPropertyName("incoming_value")]
    public string? IncomingValue { get; init; }

    public static FieldConflictResponse From(FieldConflict conflict) => new()
    {
        PersonField = conflict.PersonField,
        ExistingValue = conflict.ExistingValue,
        IncomingValue = conflict.IncomingValue,
    };
}

public sealed record ImportRowOutcomeResponse
{
    [JsonPropertyName("external_key")]
    public required string ExternalKey { get; init; }

    [JsonPropertyName("outcome")]
    public required string Outcome { get; init; }

    [JsonPropertyName("person_id")]
    public string? PersonId { get; init; }

    [JsonPropertyName("conflicts")]
    public required IReadOnlyList<FieldConflictResponse> Conflicts { get; init; }

    public static ImportRowOutcomeResponse From(ImportRowOutcome outcome) => new()
    {
        ExternalKey = outcome.ExternalKey,
        Outcome = outcome.Outcome.ToWireString(),
        PersonId = outcome.PersonId,
        Conflicts = [.. outcome.Conflicts.Select(FieldConflictResponse.From)],
    };
}

public sealed record ImportBatchResponse
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("tenant_id")]
    public required string TenantId { get; init; }

    [JsonPropertyName("mapping_id")]
    public required string MappingId { get; init; }

    [JsonPropertyName("mode")]
    public required string Mode { get; init; }

    [JsonPropertyName("submitted_at")]
    public required string SubmittedAt { get; init; }

    [JsonPropertyName("row_outcomes")]
    public required IReadOnlyList<ImportRowOutcomeResponse> RowOutcomes { get; init; }

    [JsonPropertyName("replayed")]
    public bool Replayed { get; init; }

    public static ImportBatchResponse From(ImportBatch batch, bool replayed = false) => new()
    {
        Id = batch.Id,
        TenantId = batch.TenantId,
        MappingId = batch.MappingId,
        Mode = batch.Mode.ToWireString(),
        SubmittedAt = batch.SubmittedAt,
        RowOutcomes = [.. batch.RowOutcomes.Select(ImportRowOutcomeResponse.From)],
        Replayed = replayed,
    };
}

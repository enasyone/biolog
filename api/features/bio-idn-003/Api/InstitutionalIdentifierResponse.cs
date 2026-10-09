using System.Text.Json.Serialization;
using Biolog.Features.InstitutionalIdentifiers.Domain;

namespace Biolog.Features.InstitutionalIdentifiers.Api;

public sealed record InstitutionalIdentifierResponse
{
    [JsonPropertyName("person_id")]
    public required string PersonId { get; init; }

    [JsonPropertyName("tenant_id")]
    public required string TenantId { get; init; }

    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("custom_label")]
    public string? CustomLabel { get; init; }

    [JsonPropertyName("sequence_number")]
    public required int SequenceNumber { get; init; }

    [JsonPropertyName("value")]
    public required string Value { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("retired_at")]
    public string? RetiredAt { get; init; }

    [JsonPropertyName("version")]
    public required int Version { get; init; }

    [JsonPropertyName("created_at")]
    public required string CreatedAt { get; init; }

    [JsonPropertyName("updated_at")]
    public required string UpdatedAt { get; init; }

    public static InstitutionalIdentifierResponse From(InstitutionalIdentifier identifier) => new()
    {
        PersonId = identifier.PersonId,
        TenantId = identifier.TenantId,
        Type = identifier.Type.ToWireString(),
        CustomLabel = identifier.CustomLabel,
        SequenceNumber = identifier.SequenceNumber,
        Value = identifier.Value,
        Status = identifier.Status.ToWireString(),
        RetiredAt = identifier.RetiredAt,
        Version = identifier.Version,
        CreatedAt = identifier.CreatedAt,
        UpdatedAt = identifier.UpdatedAt,
    };
}

public sealed record InstitutionalIdentifierListResponse
{
    [JsonPropertyName("data")]
    public required IReadOnlyList<InstitutionalIdentifierResponse> Data { get; init; }
}

public sealed record IdentifierUniquenessRuleResponse
{
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("custom_label")]
    public string? CustomLabel { get; init; }

    [JsonPropertyName("enforced")]
    public required bool Enforced { get; init; }

    /// <summary>True when no rule has ever been explicitly set for this (type, custom_label) and the codebase default (Enforced = true) is in effect - see <see cref="IdentifierUniquenessRule"/>'s header.</summary>
    [JsonPropertyName("is_default")]
    public required bool IsDefault { get; init; }

    [JsonPropertyName("version")]
    public int? Version { get; init; }

    [JsonPropertyName("created_at")]
    public string? CreatedAt { get; init; }

    [JsonPropertyName("updated_at")]
    public string? UpdatedAt { get; init; }

    public static IdentifierUniquenessRuleResponse From(IdentifierType type, string? customLabel, IdentifierUniquenessRule? rule) => new()
    {
        Type = type.ToWireString(),
        CustomLabel = customLabel,
        Enforced = rule?.Enforced ?? true,
        IsDefault = rule is null,
        Version = rule?.Version,
        CreatedAt = rule?.CreatedAt,
        UpdatedAt = rule?.UpdatedAt,
    };
}

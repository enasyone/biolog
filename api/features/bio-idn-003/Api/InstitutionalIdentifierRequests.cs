using System.Text.Json.Serialization;

namespace Biolog.Features.InstitutionalIdentifiers.Api;

/// <summary>
/// Request DTOs name exactly the fields this story owns and nothing else -
/// <see cref="InstitutionalIdentifierJsonOptions.Strict"/> makes an unrecognised JSON property
/// a deserialization failure rather than a silently-dropped one.
/// </summary>
public static class InstitutionalIdentifierJsonOptions
{
    public static readonly System.Text.Json.JsonSerializerOptions Strict = new()
    {
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower,
    };
}

/// <summary>BIO-IDN-003-AC04: no field here can name a credential id of any kind - <c>person_id</c> comes from the route, never the body.</summary>
public sealed record AssignIdentifierRequest
{
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("custom_label")]
    public string? CustomLabel { get; init; }

    [JsonPropertyName("value")]
    public string? Value { get; init; }
}

public sealed record RetireIdentifierRequest
{
    [JsonPropertyName("version")]
    public int? Version { get; init; }
}

/// <summary>BIO-IDN-003-AC02.</summary>
public sealed record SetUniquenessRuleRequest
{
    [JsonPropertyName("enforced")]
    public bool? Enforced { get; init; }
}

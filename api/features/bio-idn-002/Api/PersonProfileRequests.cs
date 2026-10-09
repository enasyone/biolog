using System.Text.Json.Serialization;

namespace Biolog.Features.PersonProfileRegistry.Api;

/// <summary>
/// Request DTOs name exactly the fields this story owns and nothing else -
/// <see cref="PersonProfileJsonOptions.Strict"/> makes an unrecognised JSON property a
/// deserialization failure rather than a silently-dropped one.
/// </summary>
public static class PersonProfileJsonOptions
{
    public static readonly System.Text.Json.JsonSerializerOptions Strict = new()
    {
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower,
    };
}

/// <summary>BIO-IDN-002-AC04: no field here can create or name a NEW person - <c>person_id</c> comes from the route, not the body.</summary>
public sealed record AssignProfileRequest
{
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("custom_label")]
    public string? CustomLabel { get; init; }

    [JsonPropertyName("organisation_id")]
    public string? OrganisationId { get; init; }

    [JsonPropertyName("campus_id")]
    public string? CampusId { get; init; }

    [JsonPropertyName("branch_id")]
    public string? BranchId { get; init; }

    [JsonPropertyName("expires_at")]
    public string? ExpiresAt { get; init; }
}

public sealed record RevokeProfileRequest
{
    [JsonPropertyName("version")]
    public int? Version { get; init; }
}

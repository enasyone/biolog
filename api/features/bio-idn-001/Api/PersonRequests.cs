using System.Text.Json.Serialization;

namespace Biolog.Features.PersonRegistry.Api;

/// <summary>
/// Request DTOs name exactly the fields this story owns and nothing else -
/// <see cref="PersonJsonOptions.Strict"/> makes an unrecognised JSON property a
/// deserialization failure rather than a silently-dropped one, the same structural-
/// enforcement posture BIO-TEN-001/BIO-TEN-002 use. This is also how BIO-IDN-001-AC01 is
/// enforced adversarially: no role/profile/institutional-identifier field exists anywhere
/// here for a caller to even attempt to set.
/// </summary>
public static class PersonJsonOptions
{
    public static readonly System.Text.Json.JsonSerializerOptions Strict = new()
    {
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower,
    };
}

/// <summary>
/// No <c>tenant_id</c> field (BIO-IDN-001-AC02): the tenant is read from the resolved tenant
/// context by <see cref="Domain.PersonService"/>, never accepted from the request body - a
/// caller cannot pick which tenant they register a person into.
/// </summary>
public sealed record RegisterPersonRequest
{
    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("first_name")]
    public string? FirstName { get; init; }

    [JsonPropertyName("middle_name")]
    public string? MiddleName { get; init; }

    [JsonPropertyName("last_name")]
    public string? LastName { get; init; }

    [JsonPropertyName("preferred_name")]
    public string? PreferredName { get; init; }

    [JsonPropertyName("born_on")]
    public string? BornOn { get; init; }
}

public sealed record UpdatePersonStatusRequest
{
    [JsonPropertyName("version")]
    public int? Version { get; init; }

    [JsonPropertyName("status")]
    public string? Status { get; init; }
}

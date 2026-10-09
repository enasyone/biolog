namespace Biolog.Features.AccessPolicy.Api;

/// <summary>
/// Request DTOs name exactly the fields this story owns and nothing else -
/// <see cref="Strict"/> makes an unrecognised JSON property a deserialization failure rather
/// than a silently-dropped one.
/// </summary>
public static class AccessPolicyJsonOptions
{
    public static readonly System.Text.Json.JsonSerializerOptions Strict = new()
    {
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower,
    };
}

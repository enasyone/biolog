namespace Biolog.Features.VisitorCredentials.Api;

/// <summary>Request DTOs name exactly the fields this story owns and nothing else - <see cref="Strict"/> makes an unrecognised JSON property a deserialization failure rather than a silently-dropped one.</summary>
public static class VisitorCredentialJsonOptions
{
    public static readonly System.Text.Json.JsonSerializerOptions Strict = new()
    {
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower,
    };

    /// <summary>Lenient variant for response serialization (no unmapped-member concern on output).</summary>
    public static readonly System.Text.Json.JsonSerializerOptions Response = new()
    {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower,
    };
}

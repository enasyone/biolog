using System.Text.Json.Serialization;

namespace Biolog.Features.DuplicateReview.Api;

/// <summary>
/// Request DTOs name exactly the fields this story owns and nothing else -
/// <see cref="DuplicateCandidateJsonOptions.Strict"/> makes an unrecognised JSON property a
/// deserialization failure rather than a silently-dropped one.
/// </summary>
public static class DuplicateCandidateJsonOptions
{
    public static readonly System.Text.Json.JsonSerializerOptions Strict = new()
    {
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower,
    };
}

/// <summary>
/// BIO-IDN-005-AC02: a review decision requires a non-blank reason and an asserted actor (see
/// <see cref="ActorResolver"/>). BIO-IDN-005-AC03: <see cref="Decision"/> is one of
/// <c>CONFIRMED_DUPLICATE</c>/<c>CONFIRMED_DISTINCT</c> - never a merge action, which does not
/// exist anywhere in this feature (see <c>Domain.DuplicateReviewStatus</c>'s header).
/// </summary>
public sealed record DecideDuplicateCandidateRequest
{
    [JsonPropertyName("decision")]
    public string? Decision { get; init; }

    [JsonPropertyName("reason_code")]
    public string? ReasonCode { get; init; }

    [JsonPropertyName("notes")]
    public string? Notes { get; init; }

    [JsonPropertyName("version")]
    public int? Version { get; init; }
}

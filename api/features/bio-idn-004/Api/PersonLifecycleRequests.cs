using System.Text.Json.Serialization;

namespace Biolog.Features.PersonLifecycle.Api;

/// <summary>
/// Request DTOs name exactly the fields this story owns and nothing else -
/// <see cref="PersonLifecycleJsonOptions.Strict"/> makes an unrecognised JSON property a
/// deserialization failure rather than a silently-dropped one.
/// </summary>
public static class PersonLifecycleJsonOptions
{
    public static readonly System.Text.Json.JsonSerializerOptions Strict = new()
    {
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower,
    };
}

/// <summary>BIO-IDN-004-AC01: <c>target_status</c> is one of the 8 known wire values, validated against <see cref="Domain.LifecycleTransitionPolicy"/> - never an arbitrary string passed straight through.</summary>
public sealed record TransitionRequest
{
    [JsonPropertyName("target_status")]
    public string? TargetStatus { get; init; }

    [JsonPropertyName("version")]
    public int? Version { get; init; }

    /// <summary>Required for reactivation (BIO-IDN-004-AC03); optional but recorded when supplied for every other transition, including emergency suspension (AC02).</summary>
    [JsonPropertyName("reason")]
    public string? Reason { get; init; }
}

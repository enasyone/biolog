using System.Text.Json.Serialization;

namespace Biolog.Features.ImportConflictReview.Api;

public sealed record DecideImportConflictReviewRequest
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

/// <summary>ADR-0004 §5.2: strict, unknown-member-rejecting deserialization, same convention every sibling feature's own requests use.</summary>
public static class ImportConflictReviewJsonOptions
{
    public static readonly System.Text.Json.JsonSerializerOptions Strict = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower,
    };
}

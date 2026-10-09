using System.Text.Json.Serialization;

namespace Biolog.Features.DuplicateReview.Api;

/// <summary>BIO-IDN-005-QA-01: email/phone recorded specifically for duplicate-matching purposes - see Domain.PersonContactPoint's header. Both optional; omitting a field clears it.</summary>
public sealed record SetContactPointRequest
{
    [JsonPropertyName("email")]
    public string? Email { get; init; }

    [JsonPropertyName("phone")]
    public string? Phone { get; init; }

    [JsonPropertyName("version")]
    public int? Version { get; init; }
}

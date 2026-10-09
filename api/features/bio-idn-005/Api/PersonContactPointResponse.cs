using System.Text.Json.Serialization;
using Biolog.Features.DuplicateReview.Domain;

namespace Biolog.Features.DuplicateReview.Api;

public sealed record PersonContactPointResponse
{
    [JsonPropertyName("tenant_id")]
    public required string TenantId { get; init; }

    [JsonPropertyName("person_id")]
    public required string PersonId { get; init; }

    [JsonPropertyName("email")]
    public string? Email { get; init; }

    [JsonPropertyName("phone")]
    public string? Phone { get; init; }

    [JsonPropertyName("version")]
    public required int Version { get; init; }

    [JsonPropertyName("created_at")]
    public required string CreatedAt { get; init; }

    [JsonPropertyName("updated_at")]
    public required string UpdatedAt { get; init; }

    public static PersonContactPointResponse From(PersonContactPoint contactPoint) => new()
    {
        TenantId = contactPoint.TenantId,
        PersonId = contactPoint.PersonId,
        Email = contactPoint.Email,
        Phone = contactPoint.Phone,
        Version = contactPoint.Version,
        CreatedAt = contactPoint.CreatedAt,
        UpdatedAt = contactPoint.UpdatedAt,
    };
}

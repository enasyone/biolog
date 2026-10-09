using System.Text.Json.Serialization;
using Biolog.Features.PersonRegistry.Domain;

namespace Biolog.Features.PersonRegistry.Api;

public sealed record PersonResponse
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("tenant_id")]
    public required string TenantId { get; init; }

    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("first_name")]
    public required string FirstName { get; init; }

    [JsonPropertyName("middle_name")]
    public string? MiddleName { get; init; }

    [JsonPropertyName("last_name")]
    public required string LastName { get; init; }

    [JsonPropertyName("preferred_name")]
    public string? PreferredName { get; init; }

    /// <summary>Derived on read from Title/FirstName/MiddleName/LastName - see <see cref="PersonNameFormatter"/>. Never stored.</summary>
    [JsonPropertyName("full_name")]
    public required string FullName { get; init; }

    [JsonPropertyName("born_on")]
    public string? BornOn { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("version")]
    public required int Version { get; init; }

    [JsonPropertyName("created_at")]
    public required string CreatedAt { get; init; }

    [JsonPropertyName("updated_at")]
    public required string UpdatedAt { get; init; }

    public static PersonResponse From(Person person) => new()
    {
        Id = person.Id,
        TenantId = person.TenantId,
        Title = person.Title,
        FirstName = person.FirstName,
        MiddleName = person.MiddleName,
        LastName = person.LastName,
        PreferredName = person.PreferredName,
        FullName = PersonNameFormatter.FullName(person),
        BornOn = person.BornOn,
        Status = person.Status.ToWireString(),
        Version = person.Version,
        CreatedAt = person.CreatedAt,
        UpdatedAt = person.UpdatedAt,
    };
}

public sealed record PersonListResponse
{
    [JsonPropertyName("data")]
    public required IReadOnlyList<PersonResponse> Data { get; init; }
}

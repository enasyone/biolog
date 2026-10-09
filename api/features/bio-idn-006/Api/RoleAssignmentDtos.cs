using System.Text.Json.Serialization;
using Biolog.Features.Rbac.Domain;

namespace Biolog.Features.Rbac.Api;

public sealed record RevokeRoleRequest
{
    [JsonPropertyName("version")]
    public int? Version { get; init; }
}

public sealed record PersonRoleAssignmentResponse
{
    [JsonPropertyName("tenant_id")]
    public required string TenantId { get; init; }

    [JsonPropertyName("person_id")]
    public required string PersonId { get; init; }

    [JsonPropertyName("role")]
    public required string Role { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("revoked_at")]
    public string? RevokedAt { get; init; }

    [JsonPropertyName("version")]
    public required int Version { get; init; }

    [JsonPropertyName("created_at")]
    public required string CreatedAt { get; init; }

    [JsonPropertyName("updated_at")]
    public required string UpdatedAt { get; init; }

    public static PersonRoleAssignmentResponse From(PersonRoleAssignment assignment) => new()
    {
        TenantId = assignment.TenantId,
        PersonId = assignment.PersonId,
        Role = assignment.Role.ToWireString(),
        Status = assignment.Status.ToWireString(),
        RevokedAt = assignment.RevokedAt,
        Version = assignment.Version,
        CreatedAt = assignment.CreatedAt,
        UpdatedAt = assignment.UpdatedAt,
    };
}

public sealed record PersonRoleAssignmentListResponse
{
    [JsonPropertyName("data")]
    public required IReadOnlyList<PersonRoleAssignmentResponse> Data { get; init; }
}

public sealed record RoleCatalogueEntry
{
    [JsonPropertyName("role")]
    public required string Role { get; init; }
}

public sealed record RoleCatalogueResponse
{
    [JsonPropertyName("data")]
    public required IReadOnlyList<RoleCatalogueEntry> Data { get; init; }
}

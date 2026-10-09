using System.Text.Json.Serialization;
using Biolog.Features.Rbac.Domain;

namespace Biolog.Features.Rbac.Api;

public sealed record SetPermissionGrantRequest
{
    [JsonPropertyName("granted")]
    public bool? Granted { get; init; }
}

public sealed record RolePermissionGrantResponse
{
    [JsonPropertyName("role")]
    public required string Role { get; init; }

    [JsonPropertyName("permission_key")]
    public required string PermissionKey { get; init; }

    [JsonPropertyName("granted")]
    public required bool Granted { get; init; }

    /// <summary>True when SUPER_ADMIN's implicit, non-configurable grant is why this reads as granted, or when no explicit grant record exists yet and the fail-closed default (not granted) is in effect.</summary>
    [JsonPropertyName("is_default")]
    public required bool IsDefault { get; init; }

    [JsonPropertyName("version")]
    public int? Version { get; init; }

    [JsonPropertyName("created_at")]
    public string? CreatedAt { get; init; }

    [JsonPropertyName("updated_at")]
    public string? UpdatedAt { get; init; }

    public static RolePermissionGrantResponse From(RoleType role, string permissionKey, RolePermissionGrant? grant) => new()
    {
        Role = role.ToWireString(),
        PermissionKey = permissionKey,
        Granted = role == RoleType.SuperAdmin || (grant?.Granted ?? false),
        IsDefault = role == RoleType.SuperAdmin || grant is null,
        Version = grant?.Version,
        CreatedAt = grant?.CreatedAt,
        UpdatedAt = grant?.UpdatedAt,
    };
}

public sealed record PermissionCheckResponse
{
    [JsonPropertyName("person_id")]
    public required string PersonId { get; init; }

    [JsonPropertyName("permission_key")]
    public required string PermissionKey { get; init; }

    [JsonPropertyName("granted")]
    public required bool Granted { get; init; }

    [JsonPropertyName("via_roles")]
    public required IReadOnlyList<string> ViaRoles { get; init; }
}

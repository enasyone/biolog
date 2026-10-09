using System.Text.Json.Serialization;
using Biolog.Features.TenantRegistry.Domain;

namespace Biolog.Features.TenantRegistry.Api;

public sealed record TenantResponse
{
    [JsonPropertyName("id")] public required string Id { get; init; }
    [JsonPropertyName("name")] public required string Name { get; init; }
    [JsonPropertyName("status")] public required string Status { get; init; }
    [JsonPropertyName("license_plan")] public required string LicensePlan { get; init; }
    [JsonPropertyName("license_seats")] public required int LicenseSeats { get; init; }
    [JsonPropertyName("routing_region")] public required string RoutingRegion { get; init; }
    [JsonPropertyName("version")] public required int Version { get; init; }
    [JsonPropertyName("created_at")] public required string CreatedAt { get; init; }
    [JsonPropertyName("updated_at")] public required string UpdatedAt { get; init; }

    public static TenantResponse From(Tenant tenant) => new()
    {
        Id = tenant.Id,
        Name = tenant.Name,
        Status = tenant.Status.ToWireString(),
        LicensePlan = tenant.LicensePlan,
        LicenseSeats = tenant.LicenseSeats,
        RoutingRegion = tenant.RoutingRegion,
        Version = tenant.Version,
        CreatedAt = tenant.CreatedAt,
        UpdatedAt = tenant.UpdatedAt,
    };
}

/// <summary><c>contracts/schemas/paginated-list-response.schema.json</c> (ADR-0004 §5.1).</summary>
public sealed record TenantListResponse
{
    [JsonPropertyName("data")] public required IReadOnlyList<TenantResponse> Data { get; init; }
    [JsonPropertyName("page")] public required PageInfo Page { get; init; }
}

public sealed record PageInfo
{
    [JsonPropertyName("next_cursor")] public required string? NextCursor { get; init; }
    [JsonPropertyName("limit")] public required int Limit { get; init; }
}

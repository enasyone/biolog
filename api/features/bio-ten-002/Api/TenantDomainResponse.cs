using System.Text.Json.Serialization;
using Biolog.Features.TenantDomainRegistry.Domain;

namespace Biolog.Features.TenantDomainRegistry.Api;

/// <summary>
/// BIO-TEN-002-AC05: this response carries exactly <c>contracts/schemas/tenant-domain.schema.json</c>'s
/// fields plus versioning metadata - no A-record, CNAME, IP address or any other DNS
/// deployment detail. That separation is not something this class checks; it is what its
/// fixed property list already guarantees, the same way <see cref="TenantDomainRequests"/>'s
/// field list guarantees BIO-TEN-002-AC01's shape.
/// </summary>
public sealed record TenantDomainResponse
{
    [JsonPropertyName("hostname")] public required string Hostname { get; init; }
    [JsonPropertyName("tenant_id")] public required string TenantId { get; init; }
    [JsonPropertyName("status")] public required string Status { get; init; }
    [JsonPropertyName("is_primary")] public required bool IsPrimary { get; init; }
    [JsonPropertyName("version")] public required int Version { get; init; }
    [JsonPropertyName("created_at")] public required string CreatedAt { get; init; }
    [JsonPropertyName("updated_at")] public required string UpdatedAt { get; init; }

    public static TenantDomainResponse From(TenantDomain domain) => new()
    {
        Hostname = domain.Hostname,
        TenantId = domain.TenantId,
        Status = domain.Status.ToWireString(),
        IsPrimary = domain.IsPrimary,
        Version = domain.Version,
        CreatedAt = domain.CreatedAt,
        UpdatedAt = domain.UpdatedAt,
    };
}

public sealed record TenantDomainListResponse
{
    [JsonPropertyName("data")] public required IReadOnlyList<TenantDomainResponse> Data { get; init; }
}

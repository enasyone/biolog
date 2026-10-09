using System.Text.Json.Serialization;

namespace Biolog.Features.TenantDomainRegistry.Api;

/// <summary>
/// Request DTOs for the tenant domain registry. Each DTO names exactly the fields this story
/// owns and nothing else - <see cref="TenantDomainJsonOptions.Strict"/> makes an unrecognised
/// JSON property a deserialization failure rather than a silently-dropped one, the same
/// structural-enforcement posture BIO-TEN-001 uses for BIO-TEN-002-AC01's control-plane
/// field-shape guarantee (AC05's separation of DNS deployment detail from application
/// routing is enforced the same way, by omission: no A-record/CNAME field exists anywhere
/// here for a caller to even attempt to set).
/// </summary>
public static class TenantDomainJsonOptions
{
    public static readonly System.Text.Json.JsonSerializerOptions Strict = new()
    {
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower,
    };
}

public sealed record RegisterDomainRequest
{
    [JsonPropertyName("hostname")]
    public string? Hostname { get; init; }

    [JsonPropertyName("tenant_id")]
    public string? TenantId { get; init; }
}

public sealed record UpdateDomainStatusRequest
{
    [JsonPropertyName("version")]
    public int? Version { get; init; }

    [JsonPropertyName("status")]
    public string? Status { get; init; }
}

public sealed record SetPrimaryDomainRequest
{
    [JsonPropertyName("version")]
    public int? Version { get; init; }
}

using System.Text.Json.Serialization;

namespace Biolog.Features.TenantRegistry.Api;

/// <summary>
/// Request DTOs for the tenant registry. BIO-TEN-001-AC02's enforcement mechanism lives
/// here, structurally: each DTO names exactly the control-plane fields this story owns
/// (configuration, licensing, routing metadata) and nothing else. There is no free-form
/// dictionary a caller could add an operational person/credential field to, and
/// <see cref="TenantJsonOptions.Strict"/> makes an unrecognised JSON property (e.g.
/// <c>date_of_birth</c>, <c>biometric_template</c>, <c>ssn</c>) a deserialization failure
/// rather than a silently-dropped one - see <c>ControlPlaneScopeTests</c> for the adversarial
/// tests this is built to pass.
/// </summary>
public static class TenantJsonOptions
{
    public static readonly System.Text.Json.JsonSerializerOptions Strict = new()
    {
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower,
    };
}

public sealed record CreateTenantRequest
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("status")]
    public string? Status { get; init; }

    [JsonPropertyName("license_plan")]
    public string? LicensePlan { get; init; }

    [JsonPropertyName("license_seats")]
    public int? LicenseSeats { get; init; }

    [JsonPropertyName("routing_region")]
    public string? RoutingRegion { get; init; }
}

public sealed record UpdateTenantRequest
{
    [JsonPropertyName("version")]
    public int? Version { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("license_plan")]
    public string? LicensePlan { get; init; }

    [JsonPropertyName("license_seats")]
    public int? LicenseSeats { get; init; }

    [JsonPropertyName("routing_region")]
    public string? RoutingRegion { get; init; }
}

public sealed record UpdateTenantStatusRequest
{
    [JsonPropertyName("version")]
    public int? Version { get; init; }

    [JsonPropertyName("status")]
    public string? Status { get; init; }
}

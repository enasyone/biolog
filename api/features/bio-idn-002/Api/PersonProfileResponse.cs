using System.Text.Json.Serialization;
using Biolog.Features.PersonProfileRegistry.Domain;
using Biolog.Features.PersonProfileRegistry.Identifiers;

namespace Biolog.Features.PersonProfileRegistry.Api;

public sealed record PersonProfileResponse
{
    [JsonPropertyName("person_id")]
    public required string PersonId { get; init; }

    [JsonPropertyName("tenant_id")]
    public required string TenantId { get; init; }

    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("custom_label")]
    public string? CustomLabel { get; init; }

    [JsonPropertyName("organisation_id")]
    public string? OrganisationId { get; init; }

    [JsonPropertyName("campus_id")]
    public string? CampusId { get; init; }

    [JsonPropertyName("branch_id")]
    public string? BranchId { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("activated_at")]
    public required string ActivatedAt { get; init; }

    [JsonPropertyName("expires_at")]
    public string? ExpiresAt { get; init; }

    /// <summary>BIO-IDN-002-AC03: computed at response time, never stored - see <see cref="PersonProfile.IsCurrentlyInEffect"/>.</summary>
    [JsonPropertyName("is_currently_in_effect")]
    public required bool IsCurrentlyInEffect { get; init; }

    [JsonPropertyName("version")]
    public required int Version { get; init; }

    [JsonPropertyName("created_at")]
    public required string CreatedAt { get; init; }

    [JsonPropertyName("updated_at")]
    public required string UpdatedAt { get; init; }

    public static PersonProfileResponse From(PersonProfile profile) => new()
    {
        PersonId = profile.PersonId,
        TenantId = profile.TenantId,
        Type = profile.Type.ToWireString(),
        CustomLabel = profile.CustomLabel,
        OrganisationId = profile.OrganisationId,
        CampusId = profile.CampusId,
        BranchId = profile.BranchId,
        Status = profile.Status.ToWireString(),
        ActivatedAt = profile.ActivatedAt,
        ExpiresAt = profile.ExpiresAt,
        IsCurrentlyInEffect = profile.IsCurrentlyInEffect(UtcInstant.Now()),
        Version = profile.Version,
        CreatedAt = profile.CreatedAt,
        UpdatedAt = profile.UpdatedAt,
    };
}

public sealed record PersonProfileListResponse
{
    [JsonPropertyName("data")]
    public required IReadOnlyList<PersonProfileResponse> Data { get; init; }
}

using System.Text.Json.Serialization;
using Biolog.Features.Photography.Domain;

namespace Biolog.Features.Photography.Api;

/// <summary>
/// BIO-PHO-001-AC01/AC02 response. Deliberately carries no photo bytes - metadata only, the
/// same "the response describes the record, a separate concern serves the bytes" boundary
/// <c>BrandAssetResponse</c> (BIO-CRD-001) already established. No field here is classified
/// higher than the ADR-0003 <c>personal</c> "photo reference" the person/version pair already
/// is when read back by an authenticated, tenant-scoped caller through this endpoint - the
/// audit-trail restriction (never carry it in <c>Detail</c>) is a different rule from what this
/// response, itself the authorized read path, may return.
/// </summary>
public sealed record PhotoMasterResponse
{
    [JsonPropertyName("person_id")]
    public required string PersonId { get; init; }

    [JsonPropertyName("tenant_id")]
    public required string TenantId { get; init; }

    [JsonPropertyName("version")]
    public required int Version { get; init; }

    [JsonPropertyName("format")]
    public required string Format { get; init; }

    [JsonPropertyName("width_px")]
    public required int WidthPx { get; init; }

    [JsonPropertyName("height_px")]
    public required int HeightPx { get; init; }

    [JsonPropertyName("size_bytes")]
    public required int SizeBytes { get; init; }

    [JsonPropertyName("created_at")]
    public required string CreatedAt { get; init; }

    public static PhotoMasterResponse From(PhotoMaster master) => new()
    {
        PersonId = master.PersonId,
        TenantId = master.TenantId,
        Version = master.Version,
        Format = master.Format.ToWireString(),
        WidthPx = master.WidthPx,
        HeightPx = master.HeightPx,
        SizeBytes = master.SizeBytes,
        CreatedAt = master.CreatedAt,
    };
}

public sealed record PhotoMasterListResponse
{
    [JsonPropertyName("data")]
    public required IReadOnlyList<PhotoMasterResponse> Data { get; init; }
}

public sealed record PhotoDerivativeResponse
{
    [JsonPropertyName("person_id")]
    public required string PersonId { get; init; }

    [JsonPropertyName("tenant_id")]
    public required string TenantId { get; init; }

    [JsonPropertyName("master_version")]
    public required int MasterVersion { get; init; }

    [JsonPropertyName("kind")]
    public required string Kind { get; init; }

    [JsonPropertyName("format")]
    public required string Format { get; init; }

    [JsonPropertyName("width_px")]
    public required int WidthPx { get; init; }

    [JsonPropertyName("height_px")]
    public required int HeightPx { get; init; }

    [JsonPropertyName("generated_at")]
    public required string GeneratedAt { get; init; }

    [JsonPropertyName("generator_tag")]
    public required string GeneratorTag { get; init; }

    public static PhotoDerivativeResponse From(PhotoDerivative derivative) => new()
    {
        PersonId = derivative.PersonId,
        TenantId = derivative.TenantId,
        MasterVersion = derivative.MasterVersion,
        Kind = derivative.Kind.ToWireString(),
        Format = derivative.Format.ToWireString(),
        WidthPx = derivative.WidthPx,
        HeightPx = derivative.HeightPx,
        GeneratedAt = derivative.GeneratedAt,
        GeneratorTag = derivative.GeneratorTag,
    };
}

public sealed record PhotoDerivativeListResponse
{
    [JsonPropertyName("data")]
    public required IReadOnlyList<PhotoDerivativeResponse> Data { get; init; }
}

public sealed record PhotoAssignmentResponse
{
    [JsonPropertyName("person_id")]
    public required string PersonId { get; init; }

    [JsonPropertyName("tenant_id")]
    public required string TenantId { get; init; }

    [JsonPropertyName("current_master_version")]
    public required int? CurrentMasterVersion { get; init; }

    [JsonPropertyName("version")]
    public required int Version { get; init; }

    [JsonPropertyName("created_at")]
    public required string CreatedAt { get; init; }

    [JsonPropertyName("updated_at")]
    public required string UpdatedAt { get; init; }

    public static PhotoAssignmentResponse From(PhotoAssignment assignment) => new()
    {
        PersonId = assignment.PersonId,
        TenantId = assignment.TenantId,
        CurrentMasterVersion = assignment.CurrentMasterVersion,
        Version = assignment.Version,
        CreatedAt = assignment.CreatedAt,
        UpdatedAt = assignment.UpdatedAt,
    };
}

/// <summary>Body of <c>PUT /api/v1/people/{personId}/photo-assignment</c> (ADR-0001 §3.2: <c>expected_version</c> absent means "I believe none exists yet").</summary>
public sealed record AssignPhotoRequest
{
    [JsonPropertyName("master_version")]
    public required int MasterVersion { get; init; }

    [JsonPropertyName("expected_version")]
    public int? ExpectedVersion { get; init; }
}

using System.Text.Json.Serialization;

namespace Biolog.Features.TenantFeatureConfig.Api;

/// <summary>
/// Request DTOs for the tenant feature configuration. Every field is nullable/optional:
/// <c>null</c> means "leave this field unchanged" (same convention as BIO-TEN-001's own
/// <c>UpdateTenantRequest</c>) - there is no way to express "unset back to default" other
/// than explicitly sending <c>false</c>. <see cref="TenantFeatureConfigJsonOptions.Strict"/>
/// rejects any unrecognised JSON property, the same AC-safety discipline BIO-TEN-001-AC02's
/// own strict request DTOs established.
/// </summary>
public static class TenantFeatureConfigJsonOptions
{
    public static readonly System.Text.Json.JsonSerializerOptions Strict = new()
    {
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower,
    };
}

public sealed record BiometricsConfigRequest
{
    [JsonPropertyName("enabled")]
    public bool? Enabled { get; init; }

    [JsonPropertyName("face_1n_identification_enabled")]
    public bool? Face1NIdentificationEnabled { get; init; }
}

public sealed record DocumentVerifyConfigRequest
{
    [JsonPropertyName("enabled")]
    public bool? Enabled { get; init; }

    [JsonPropertyName("capture_enabled")]
    public bool? CaptureEnabled { get; init; }

    [JsonPropertyName("mrz_enabled")]
    public bool? MrzEnabled { get; init; }

    [JsonPropertyName("barcode_enabled")]
    public bool? BarcodeEnabled { get; init; }

    [JsonPropertyName("ocr_enabled")]
    public bool? OcrEnabled { get; init; }

    [JsonPropertyName("authenticity_enabled")]
    public bool? AuthenticityEnabled { get; init; }

    [JsonPropertyName("document_identification_enabled")]
    public bool? DocumentIdentificationEnabled { get; init; }

    [JsonPropertyName("nfc_enabled")]
    public bool? NfcEnabled { get; init; }

    [JsonPropertyName("selfie_comparison_enabled")]
    public bool? SelfieComparisonEnabled { get; init; }
}

public sealed record UpdateTenantFeatureConfigurationRequest
{
    /// <summary>Required (ADR-0001 §3.1 optimistic concurrency) - <c>0</c> targets the
    /// synthesized "never written" default (<see cref="Domain.TenantFeatureConfiguration.Default"/>),
    /// so the very first write for a tenant is expressed the same way as every later one.</summary>
    [JsonPropertyName("version")]
    public int? Version { get; init; }

    [JsonPropertyName("biometrics")]
    public BiometricsConfigRequest? Biometrics { get; init; }

    [JsonPropertyName("card_production_enabled")]
    public bool? CardProductionEnabled { get; init; }

    [JsonPropertyName("visitor_enabled")]
    public bool? VisitorEnabled { get; init; }

    [JsonPropertyName("attendance_enabled")]
    public bool? AttendanceEnabled { get; init; }

    [JsonPropertyName("transport_enabled")]
    public bool? TransportEnabled { get; init; }

    [JsonPropertyName("document_verify")]
    public DocumentVerifyConfigRequest? DocumentVerify { get; init; }
}

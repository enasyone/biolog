using System.Text.Json.Serialization;
using Biolog.Features.TenantFeatureConfig.Domain;

namespace Biolog.Features.TenantFeatureConfig.Api;

public sealed record BiometricsConfigResponse
{
    [JsonPropertyName("enabled")] public required bool Enabled { get; init; }
    [JsonPropertyName("face_1n_identification_enabled")] public required bool Face1NIdentificationEnabled { get; init; }

    public static BiometricsConfigResponse From(BiometricsFeatureConfig config) => new()
    {
        Enabled = config.Enabled,
        Face1NIdentificationEnabled = config.Face1NIdentificationEnabled,
    };
}

public sealed record DocumentVerifyConfigResponse
{
    [JsonPropertyName("enabled")] public required bool Enabled { get; init; }
    [JsonPropertyName("capture_enabled")] public required bool CaptureEnabled { get; init; }
    [JsonPropertyName("mrz_enabled")] public required bool MrzEnabled { get; init; }
    [JsonPropertyName("barcode_enabled")] public required bool BarcodeEnabled { get; init; }
    [JsonPropertyName("ocr_enabled")] public required bool OcrEnabled { get; init; }
    [JsonPropertyName("authenticity_enabled")] public required bool AuthenticityEnabled { get; init; }
    [JsonPropertyName("document_identification_enabled")] public required bool DocumentIdentificationEnabled { get; init; }
    [JsonPropertyName("nfc_enabled")] public required bool NfcEnabled { get; init; }
    [JsonPropertyName("selfie_comparison_enabled")] public required bool SelfieComparisonEnabled { get; init; }

    public static DocumentVerifyConfigResponse From(DocumentVerifyFeatureConfig config) => new()
    {
        Enabled = config.Enabled,
        CaptureEnabled = config.CaptureEnabled,
        MrzEnabled = config.MrzEnabled,
        BarcodeEnabled = config.BarcodeEnabled,
        OcrEnabled = config.OcrEnabled,
        AuthenticityEnabled = config.AuthenticityEnabled,
        DocumentIdentificationEnabled = config.DocumentIdentificationEnabled,
        NfcEnabled = config.NfcEnabled,
        SelfieComparisonEnabled = config.SelfieComparisonEnabled,
    };
}

public sealed record TenantFeatureConfigurationResponse
{
    [JsonPropertyName("tenant_id")] public required string TenantId { get; init; }
    [JsonPropertyName("biometrics")] public required BiometricsConfigResponse Biometrics { get; init; }
    [JsonPropertyName("card_production_enabled")] public required bool CardProductionEnabled { get; init; }
    [JsonPropertyName("visitor_enabled")] public required bool VisitorEnabled { get; init; }
    [JsonPropertyName("attendance_enabled")] public required bool AttendanceEnabled { get; init; }
    [JsonPropertyName("transport_enabled")] public required bool TransportEnabled { get; init; }
    [JsonPropertyName("document_verify")] public required DocumentVerifyConfigResponse DocumentVerify { get; init; }
    [JsonPropertyName("version")] public required int Version { get; init; }
    [JsonPropertyName("created_at")] public required string CreatedAt { get; init; }
    [JsonPropertyName("updated_at")] public required string UpdatedAt { get; init; }

    public static TenantFeatureConfigurationResponse From(TenantFeatureConfiguration config) => new()
    {
        TenantId = config.TenantId,
        Biometrics = BiometricsConfigResponse.From(config.Biometrics),
        CardProductionEnabled = config.CardProductionEnabled,
        VisitorEnabled = config.VisitorEnabled,
        AttendanceEnabled = config.AttendanceEnabled,
        TransportEnabled = config.TransportEnabled,
        DocumentVerify = DocumentVerifyConfigResponse.From(config.DocumentVerify),
        Version = config.Version,
        CreatedAt = config.CreatedAt,
        UpdatedAt = config.UpdatedAt,
    };
}

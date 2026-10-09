using System.Text.Json;
using System.Text.Json.Serialization;
using Biolog.Features.CardObjects.Domain;

namespace Biolog.Features.CardObjects.Api;

public sealed record BarcodeProfileEntry
{
    [JsonPropertyName("profile")]
    public required string Profile { get; init; }

    [JsonPropertyName("is_default")]
    public required bool IsDefault { get; init; }
}

/// <summary>BIO-CRD-004-AC01/AC02: the full default+optional profile catalogue in one read.</summary>
public sealed record BarcodeProfileCatalogResponse
{
    [JsonPropertyName("default_profile")]
    public required string DefaultProfile { get; init; }

    [JsonPropertyName("data")]
    public required IReadOnlyList<BarcodeProfileEntry> Data { get; init; }

    public static BarcodeProfileCatalogResponse Build() => new()
    {
        DefaultProfile = BarcodeProfileCatalog.DefaultProfile.ToWireString(),
        Data = [.. BarcodeProfileCatalog.AllProfiles.Select(profile => new BarcodeProfileEntry
        {
            Profile = profile.ToWireString(),
            IsDefault = BarcodeProfileCatalog.IsDefault(profile),
        })],
    };
}

public sealed record ValidateBarcodeRequest
{
    [JsonPropertyName("profile")]
    public required string Profile { get; init; }

    [JsonPropertyName("quiet_zone_mm")]
    public required double QuietZoneMm { get; init; }

    [JsonPropertyName("width_mm")]
    public required double WidthMm { get; init; }

    [JsonPropertyName("height_mm")]
    public required double HeightMm { get; init; }

    [JsonPropertyName("foreground_color_hex")]
    public required string ForegroundColorHex { get; init; }

    [JsonPropertyName("background_color_hex")]
    public required string BackgroundColorHex { get; init; }
}

/// <summary>BIO-CRD-004-AC03: a validation verdict is DATA (200), never an HTTP error, for a well-formed request - the same convention BIO-CRD-002-AC03's custom-size validator established.</summary>
public sealed record ValidateBarcodeResponse
{
    [JsonPropertyName("is_valid")]
    public required bool IsValid { get; init; }

    [JsonPropertyName("violations")]
    public required IReadOnlyList<string> Violations { get; init; }

    public static ValidateBarcodeResponse From(BarcodeValidationResult result) => new()
    {
        IsValid = result.IsValid,
        Violations = result.Violations,
    };
}

public static class ValidateBarcodeRequestJson
{
    public static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
}

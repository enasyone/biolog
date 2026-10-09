using System.Text.Json;
using System.Text.Json.Serialization;

namespace Biolog.Api.Features.BrandAssets;

public static class BrandAssetJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}

public sealed record RasterQualityResponse(
    int PixelWidth,
    int PixelHeight,
    bool HasAlphaChannel,
    double? NativeDpi,
    double? EffectiveDpi,
    bool LowResolutionWarning,
    string? WarningMessage);

public sealed record BrandAssetResponse(
    string Id,
    string LineageId,
    int Version,
    string TenantId,
    string? OrganisationId,
    string? CampusId,
    string AssetType,
    string Status,
    string Format,
    string FileName,
    string ContentType,
    int SizeBytes,
    RasterQualityResponse? RasterQuality,
    bool ContentValid,
    IReadOnlyList<string> ContentValidationMessages,
    string MalwareScanVerdict,
    IReadOnlyList<string> MalwareScanFindings,
    string UploadedBy,
    DateTimeOffset CreatedAtUtc)
{
    public static BrandAssetResponse From(BrandAsset asset) => new(
        asset.Id,
        asset.LineageId,
        asset.Version,
        asset.TenantId,
        asset.OrganisationId,
        asset.CampusId,
        asset.AssetType.ToString(),
        asset.Status.ToString(),
        asset.Format.ToString(),
        asset.FileName,
        asset.ContentType,
        asset.SizeBytes,
        asset.RasterQuality is null
            ? null
            : new RasterQualityResponse(
                asset.RasterQuality.PixelWidth,
                asset.RasterQuality.PixelHeight,
                asset.RasterQuality.HasAlphaChannel,
                asset.RasterQuality.NativeDpi,
                asset.RasterQuality.EffectiveDpi,
                asset.RasterQuality.LowResolutionWarning,
                asset.RasterQuality.WarningMessage),
        asset.ContentValidation.IsValid,
        asset.ContentValidation.Messages,
        asset.MalwareScan.Verdict.ToString(),
        asset.MalwareScan.Findings,
        asset.UploadedBy,
        asset.CreatedAtUtc);
}

public sealed record BrandAssetPageResponse(IReadOnlyList<BrandAssetResponse> Data, PageInfo Page);

public sealed record PageInfo(string? NextCursor, int Limit);

public sealed record UpdateStatusRequest(string Status);

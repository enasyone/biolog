namespace Biolog.Features.CardTemplates.Domain;

/// <summary>
/// AC03: "retains ... signature rules." When <see cref="Required"/>, <see cref="LineageId"/>/
/// <see cref="Version"/> name an exact BIO-CRD-004 <c>SignatureAsset</c> row
/// (<c>(TenantId, LineageId, Version)</c>) - existence is checked live via
/// <see cref="ISignatureAssetGateway"/> (the one genuine cross-story runtime dependency this
/// story has, unlike the barcode/watermark/format vocabulary which is mirrored, not gatewayed).
/// </summary>
public sealed record SignatureRule
{
    public required bool Required { get; init; }

    public string? LineageId { get; init; }
    public int? Version { get; init; }

    public static SignatureRule NotRequired { get; } = new() { Required = false };
}

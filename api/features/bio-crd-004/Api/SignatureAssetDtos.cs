using System.Text.Json.Serialization;
using Biolog.Features.CardObjects.Domain;

namespace Biolog.Features.CardObjects.Api;

public sealed record SignatureAssetEntry
{
    [JsonPropertyName("lineage_id")]
    public required string LineageId { get; init; }

    [JsonPropertyName("version")]
    public required int Version { get; init; }

    [JsonPropertyName("size_bytes")]
    public required int SizeBytes { get; init; }

    [JsonPropertyName("created_at")]
    public required string CreatedAt { get; init; }

    public static SignatureAssetEntry From(SignatureAsset asset) => new()
    {
        LineageId = asset.LineageId,
        Version = asset.Version,
        SizeBytes = asset.Content.Length,
        CreatedAt = asset.CreatedAt,
    };
}

public sealed record SignatureAssetVersionsResponse
{
    [JsonPropertyName("data")]
    public required IReadOnlyList<SignatureAssetEntry> Data { get; init; }

    public static SignatureAssetVersionsResponse From(IReadOnlyList<SignatureAsset> assets) => new()
    {
        Data = [.. assets.Select(SignatureAssetEntry.From)],
    };
}

public sealed record AppendSignatureVersionRequest
{
    /// <summary>Base64-encoded signature image bytes - synthetic test fixtures only, never real biometric/PII content (repository policy).</summary>
    [JsonPropertyName("content_base64")]
    public required string ContentBase64 { get; init; }
}

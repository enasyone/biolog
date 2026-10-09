namespace Biolog.Features.CardObjects.Domain;

/// <summary>This feature's domain error, shaped identically to every sibling story's so it duck-types cleanly onto <see cref="Api.ProblemResponse"/>'s builder (ADR-0004 §2.1).</summary>
public sealed class CardObjectsError(string message, string code, int status, IReadOnlyDictionary<string, object?>? detail = null)
    : Exception(message)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
    public IReadOnlyDictionary<string, object?> Detail { get; } = detail ?? new Dictionary<string, object?>();

    public static CardObjectsError UnknownBarcodeProfile(string profile) => new(
        $"unknown barcode profile: {profile}", "barcode_profile_not_found", 404,
        new Dictionary<string, object?> { ["profile"] = profile });

    public static CardObjectsError SignatureVersionNotFound(string lineageId, int? version) => new(
        version is null ? $"no signature version exists for lineage '{lineageId}'" : $"signature lineage '{lineageId}' has no version {version}",
        "signature_version_not_found", 404,
        new Dictionary<string, object?> { ["lineage_id"] = lineageId, ["version"] = version });

    public static CardObjectsError TenantContextMissing() => new(
        "no tenant context resolved for this request", "tenant_context_missing", 428);
}

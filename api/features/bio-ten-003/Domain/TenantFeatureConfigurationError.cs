namespace Biolog.Features.TenantFeatureConfig.Domain;

/// <summary>
/// This feature's domain error, shaped identically to <c>TenantRegistryError</c>
/// (ADR-0004 §2.1: "(message, code, status, detail) ... without any of them extending a
/// common base") so it duck-types cleanly onto <see cref="Api.ProblemResponse"/>'s builder.
/// Copied and scoped to this feature's own error codes per this codebase's "each feature
/// keeps its own copy" convention.
/// </summary>
public sealed class TenantFeatureConfigurationError(string message, string code, int status, IReadOnlyDictionary<string, object?>? detail = null)
    : Exception(message)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
    public IReadOnlyDictionary<string, object?> Detail { get; } = detail ?? new Dictionary<string, object?>();

    public IReadOnlyList<Api.ValidationErrorItem>? Errors { get; init; }

    public static TenantFeatureConfigurationError InvalidTenantId(string tenantId) => new(
        $"'{tenantId}' is not a well-formed tenant id", "invalid_tenant_id", 400,
        new Dictionary<string, object?> { ["tenant_id"] = tenantId });

    public static TenantFeatureConfigurationError ConcurrencyConflict(string tenantId, int expected, int actual) => new(
        "the tenant's feature configuration was modified since the version this request expected", "concurrency_conflict", 409,
        new Dictionary<string, object?> { ["tenant_id"] = tenantId, ["expected_version"] = expected, ["actual_version"] = actual });

    public static TenantFeatureConfigurationError ActorRequired() => new(
        "X-Actor-Id is required to record who made this administrative change", "actor_required", 400);

    public static TenantFeatureConfigurationError InvalidActorType(string actorType) => new(
        "X-Actor-Type must be one of user, service_account, device, system, anonymous, unknown", "invalid_actor_type", 400,
        new Dictionary<string, object?> { ["actor_type"] = actorType });

    public static TenantFeatureConfigurationError Validation(IReadOnlyList<Api.ValidationErrorItem> errors) => new(
        "the request failed validation", "validation_failed", 422)
    { Errors = errors };

    public static TenantFeatureConfigurationError AuditUnavailable() => new(
        "the audit sink failed; the configuration change was not committed without an audit record", "audit_unavailable", 503);

    /// <summary>Round-2 remediation (ZB-TEN003-01/BIO-TEN-003-F01, Critical). ADR-0006 Decision 3: the asserted X-Actor-Id must carry provenance proof (X-Actor-Provenance) before it feeds an authorization decision.</summary>
    public static TenantFeatureConfigurationError ActorProvenanceRequired() => new(
        "X-Actor-Provenance is required and must verify against X-Actor-Id before this administrative action is authorized", "actor_provenance_required", 401);

    /// <summary>Round-2 remediation (ZB-TEN003-01/BIO-TEN-003-F01, Critical). The caller does not hold the required permission for this tenant.</summary>
    public static TenantFeatureConfigurationError Forbidden(string permissionKey) => new(
        "the caller does not hold the required permission for this tenant", "forbidden", 403,
        new Dictionary<string, object?> { ["permission_key"] = permissionKey });

    /// <summary>Round-2 remediation (ZB-TEN003-01/BIO-TEN-003-F01, Critical). The target tenant id does not exist in BIO-TEN-001's own control-plane registry.</summary>
    public static TenantFeatureConfigurationError TenantNotFound(string tenantId) => new(
        $"tenant '{tenantId}' does not exist", "tenant_not_found", 404,
        new Dictionary<string, object?> { ["tenant_id"] = tenantId });
}

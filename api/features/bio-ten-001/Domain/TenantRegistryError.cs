namespace Biolog.Features.TenantRegistry.Domain;

/// <summary>
/// This feature's domain error, shaped identically to <c>TenantContextError</c> /
/// <c>ConcurrencyError</c> / <c>AuditError</c> in the contracts (ADR-0004 §2.1: "(message,
/// code, status, detail) ... without any of them extending a common base") so it duck-types
/// cleanly onto <see cref="Api.ProblemResponse"/>'s builder the same way those three do onto
/// <c>toProblemResponse</c>.
/// </summary>
public sealed class TenantRegistryError(string message, string code, int status, IReadOnlyDictionary<string, object?>? detail = null)
    : Exception(message)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
    public IReadOnlyDictionary<string, object?> Detail { get; } = detail ?? new Dictionary<string, object?>();

    public IReadOnlyList<Api.ValidationErrorItem>? Errors { get; init; }

    public static TenantRegistryError NotFound(string tenantId) => new(
        $"tenant not found: {tenantId}", "tenant_not_found", 404,
        new Dictionary<string, object?> { ["id"] = tenantId });

    public static TenantRegistryError ConcurrencyConflict(string tenantId, int expected, int actual) => new(
        "the tenant was modified since the version this request expected", "concurrency_conflict", 409,
        new Dictionary<string, object?> { ["id"] = tenantId, ["expected_version"] = expected, ["actual_version"] = actual });

    public static TenantRegistryError ActorRequired() => new(
        "X-Actor-Id is required to record who made this administrative change", "actor_required", 400);

    public static TenantRegistryError InvalidActorType(string actorType) => new(
        "X-Actor-Type must be one of user, service_account, device, system, anonymous, unknown", "invalid_actor_type", 400,
        new Dictionary<string, object?> { ["actor_type"] = actorType });

    public static TenantRegistryError Validation(IReadOnlyList<Api.ValidationErrorItem> errors) => new(
        "the request failed validation", "validation_failed", 422)
    { Errors = errors };

    public static TenantRegistryError AuditUnavailable() => new(
        "the audit sink failed; the administrative change was not committed without an audit record", "audit_unavailable", 503);
}

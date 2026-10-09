namespace Biolog.Features.TenantDomainRegistry.Domain;

/// <summary>
/// This feature's domain error, shaped identically to <c>TenantContextError</c> /
/// <c>ConcurrencyError</c> / <c>AuditError</c> in the contracts (ADR-0004 §2.1: "(message,
/// code, status, detail) ... without any of them extending a common base") so it duck-types
/// cleanly onto <see cref="Api.ProblemResponse"/>'s builder the same way those three do onto
/// <c>toProblemResponse</c>.
///
/// Two distinct "not found" shapes exist on purpose:
/// <see cref="NotFound"/> is for the authenticated admin CRUD surface (register/verify/
/// suspend/retire/set-primary), which may safely say "that hostname does not exist" because
/// the caller already asserted an actor identity. <see cref="HostNotRecognised"/> is for the
/// anonymous, Host-header-driven resolution path (BIO-TEN-002-AC03) and deliberately never
/// distinguishes "no such hostname" from "hostname exists but is not VERIFIED" - ADR-0002
/// §1.3: a 403-shaped distinction there "would confirm that a hostname exists but is not yet
/// verified, which is a free enumeration oracle over the tenant registry." Both map to plain
/// 404s in HTTP terms, but only <see cref="HostNotRecognised"/> carries the "these two causes
/// must never be told apart" guarantee - do not use it in place of <see cref="NotFound"/> on
/// the admin surface, where distinguishing them is not a leak.
/// </summary>
public sealed class TenantDomainError(string message, string code, int status, IReadOnlyDictionary<string, object?>? detail = null)
    : Exception(message)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
    public IReadOnlyDictionary<string, object?> Detail { get; } = detail ?? new Dictionary<string, object?>();

    public IReadOnlyList<Api.ValidationErrorItem>? Errors { get; init; }

    public static TenantDomainError NotFound(string hostname) => new(
        $"tenant domain not found: {hostname}", "tenant_domain_not_found", 404,
        new Dictionary<string, object?> { ["hostname"] = hostname });

    /// <summary>BIO-TEN-002-AC03. Deliberately uninformative: see class header.</summary>
    public static TenantDomainError HostNotRecognised() => new(
        "host not recognised", "host_not_recognised", 404);

    /// <summary>BIO-TEN-002-AC04.</summary>
    public static TenantDomainError DuplicateHostname(string hostname) => new(
        $"hostname is already assigned: {hostname}", "duplicate_hostname", 409,
        new Dictionary<string, object?> { ["hostname"] = hostname });

    public static TenantDomainError ConcurrencyConflict(string hostname, int expected, int actual) => new(
        "the tenant domain was modified since the version this request expected", "concurrency_conflict", 409,
        new Dictionary<string, object?> { ["hostname"] = hostname, ["expected_version"] = expected, ["actual_version"] = actual });

    public static TenantDomainError ActorRequired() => new(
        "X-Actor-Id is required to record who made this administrative change", "actor_required", 400);

    public static TenantDomainError InvalidActorType(string actorType) => new(
        "X-Actor-Type must be one of user, service_account, device, system, anonymous, unknown", "invalid_actor_type", 400,
        new Dictionary<string, object?> { ["actor_type"] = actorType });

    public static TenantDomainError Validation(IReadOnlyList<Api.ValidationErrorItem> errors) => new(
        "the request failed validation", "validation_failed", 422)
    { Errors = errors };

    public static TenantDomainError AuditUnavailable() => new(
        "the audit sink failed; the administrative change was not committed without an audit record", "audit_unavailable", 503);
}

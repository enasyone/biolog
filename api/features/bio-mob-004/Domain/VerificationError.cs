namespace Biolog.Features.MobileVerification.Domain;

/// <summary>This feature's domain error, shaped identically to every sibling story's so it duck-types cleanly onto <see cref="Api.ProblemResponse"/>'s builder (ADR-0004 §2.1).</summary>
public sealed class VerificationError(string message, string code, int status, IReadOnlyDictionary<string, object?>? detail = null)
    : Exception(message)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
    public IReadOnlyDictionary<string, object?> Detail { get; } = detail ?? new Dictionary<string, object?>();

    public IReadOnlyList<Api.ValidationErrorItem>? Errors { get; init; }

    public static VerificationError Validation(IReadOnlyList<Api.ValidationErrorItem> errors) => new(
        "the request failed validation", "validation_failed", 422)
    { Errors = errors };

    public static VerificationError DeviceAuthenticationFailed() => new(
        "device authentication failed", "device_authentication_failed", 401);

    public static VerificationError IdempotencyKeyRequired() => new(
        "Idempotency-Key is required on this write endpoint (ADR-0001 §3.4)", "idempotency_key_required", 428);

    public static VerificationError TenantContextMissing() => new(
        "no tenant could be resolved for this request (ADR-0002 Decision 2)", "tenant_context_missing", 403);

    public static VerificationError ActorRequired() => new(
        "X-Actor-Id is required (ADR-0006 Decision 3, pending BIO-INT-001)", "actor_required", 401);

    public static VerificationError InvalidActorType(string value) => new(
        $"'{value}' is not a recognised actor type", "invalid_actor_type", 422);

    public static VerificationError ActorProvenanceRequired() => new(
        "X-Actor-Provenance is required and must verify against the resolved actor id (ADR-0006 Decision 3)",
        "actor_provenance_required", 401);

    public static VerificationError Forbidden(string permissionKey) => new(
        "the calling actor does not hold the required permission", "forbidden", 403,
        new Dictionary<string, object?> { ["permission_key"] = permissionKey });

    /// <summary>BIO-MOB-004-AC03: the calling device's assigned role does not permit this action.</summary>
    public static VerificationError RoleForbidden(string action, string role) => new(
        "the calling device's assigned verification role does not permit this action", "role_forbidden", 403,
        new Dictionary<string, object?> { ["action"] = action, ["role"] = role });

    public static VerificationError AuditUnavailable() => new(
        "the audit sink failed; the change was not committed without an audit record", "audit_unavailable", 503);

    public static VerificationError DownstreamDecisionServiceFailed(string detail) => new(
        "the access decision service could not evaluate this request", "downstream_decision_service_failed", 502,
        new Dictionary<string, object?> { ["detail"] = detail });

    public static VerificationError InvalidDeviceRole(string value) => new(
        $"'{value}' is not a recognised verification device role", "invalid_device_role", 422);

    /// <summary>AC03: mirrors BIO-MOB-005's own DecisionReasonRequired - there is no override path that skips recording why.</summary>
    public static VerificationError OverrideReasonRequired() => new(
        "a reason_code is required to override a computed deny", "override_reason_required", 422);
}

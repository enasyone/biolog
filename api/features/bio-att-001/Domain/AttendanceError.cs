namespace Biolog.Features.Attendance.Domain;

/// <summary>This feature's domain error, shaped identically to every sibling story's so it duck-types cleanly onto <see cref="Api.ProblemResponse"/>'s builder (ADR-0004 §2.1) - mirrors BIO-ACC-001's own <c>AccessPolicyError</c>.</summary>
public sealed class AttendanceError(string message, string code, int status, IReadOnlyDictionary<string, object?>? detail = null)
    : Exception(message)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
    public IReadOnlyDictionary<string, object?> Detail { get; } = detail ?? new Dictionary<string, object?>();

    public IReadOnlyList<Api.ValidationErrorItem>? Errors { get; init; }

    public static AttendanceError ActorRequired() => new(
        "X-Actor-Id is required to record who captured or changed this attendance data", "actor_required", 400);

    public static AttendanceError InvalidActorType(string actorType) => new(
        "X-Actor-Type must be one of user, service_account, device, system, anonymous, unknown", "invalid_actor_type", 400,
        new Dictionary<string, object?> { ["actor_type"] = actorType });

    /// <summary>ADR-0006 Decision 3: required only on the policy-management endpoint, the one authorization decision this feature makes - see <see cref="IPermissionGateway"/>'s header.</summary>
    public static AttendanceError ActorProvenanceRequired() => new(
        "this action requires a valid X-Actor-Provenance proof for the asserted X-Actor-Id (ADR-0006 Decision 3) - a server-computed HMAC bound to that specific actor id, distinct from the caller-asserted X-Actor-Id itself",
        "actor_provenance_required", 401);

    public static AttendanceError Validation(IReadOnlyList<Api.ValidationErrorItem> errors) => new(
        "the request failed validation", "validation_failed", 422)
    { Errors = errors };

    public static AttendanceError AuditUnavailable() => new(
        "the audit sink failed; the change was not committed without an audit record", "audit_unavailable", 503);

    public static AttendanceError TenantContextMissing() => new(
        "tenant_context_missing: no tenant channel has resolved a context for this request", "tenant_context_missing", 400);

    public static AttendanceError PermissionRequired(string permissionKey) => new(
        $"this action requires the calling actor to hold the '{permissionKey}' permission", "permission_required", 403,
        new Dictionary<string, object?> { ["permission_key"] = permissionKey });

    public static AttendanceError PersonNotFound(string personId) => new(
        $"person not found in the resolved tenant: {personId}", "person_not_found", 404,
        new Dictionary<string, object?> { ["person_id"] = personId });

    /// <summary>AC03 - the same idempotency key was reused with genuinely different content ("event_id_reuse"-shaped conflict, ADR-0003 Decision 4's own precedent for this exact failure mode).</summary>
    public static AttendanceError IdempotencyConflict(string idempotencyKey) => new(
        $"idempotency_key '{idempotencyKey}' was already used to capture a different event - a producer must never reuse a key for different content", "idempotency_conflict", 409,
        new Dictionary<string, object?> { ["idempotency_key"] = idempotencyKey });
}

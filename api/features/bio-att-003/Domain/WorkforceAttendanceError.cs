namespace Biolog.Features.WorkforceAttendance.Domain;

/// <summary>This feature's domain error, shaped identically to every sibling story's so it duck-types cleanly onto <see cref="Api.ProblemResponse"/>'s builder (ADR-0004 §2.1) - mirrors BIO-ATT-001's own <c>AttendanceError</c>.</summary>
public sealed class WorkforceAttendanceError(string message, string code, int status, IReadOnlyDictionary<string, object?>? detail = null)
    : Exception(message)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
    public IReadOnlyDictionary<string, object?> Detail { get; } = detail ?? new Dictionary<string, object?>();

    public IReadOnlyList<Api.ValidationErrorItem>? Errors { get; init; }

    public static WorkforceAttendanceError ActorRequired() => new(
        "X-Actor-Id is required to record who changed this workforce attendance data", "actor_required", 400);

    public static WorkforceAttendanceError InvalidActorType(string actorType) => new(
        "X-Actor-Type must be one of user, service_account, device, system, anonymous, unknown", "invalid_actor_type", 400,
        new Dictionary<string, object?> { ["actor_type"] = actorType });

    /// <summary>ADR-0006 Decision 3: required on every write this feature makes (shift management, adjustment creation) - both are authorization decisions, see <see cref="IPermissionGateway"/>'s header.</summary>
    public static WorkforceAttendanceError ActorProvenanceRequired() => new(
        "this action requires a valid X-Actor-Provenance proof for the asserted X-Actor-Id (ADR-0006 Decision 3)", "actor_provenance_required", 401);

    public static WorkforceAttendanceError Validation(IReadOnlyList<Api.ValidationErrorItem> errors) => new(
        "the request failed validation", "validation_failed", 422)
    { Errors = errors };

    public static WorkforceAttendanceError AuditUnavailable() => new(
        "the audit sink failed; the change was not committed without an audit record", "audit_unavailable", 503);

    public static WorkforceAttendanceError TenantContextMissing() => new(
        "tenant_context_missing: no tenant channel has resolved a context for this request", "tenant_context_missing", 400);

    public static WorkforceAttendanceError PermissionRequired(string permissionKey) => new(
        $"this action requires the calling actor to hold the '{permissionKey}' permission", "permission_required", 403,
        new Dictionary<string, object?> { ["permission_key"] = permissionKey });

    public static WorkforceAttendanceError PersonNotFound(string personId) => new(
        $"person not found in the resolved tenant: {personId}", "person_not_found", 404,
        new Dictionary<string, object?> { ["person_id"] = personId });

    public static WorkforceAttendanceError ShiftNotFound(string shiftId) => new(
        $"shift not found in the resolved tenant: {shiftId}", "shift_not_found", 404,
        new Dictionary<string, object?> { ["shift_id"] = shiftId });

    public static WorkforceAttendanceError ConcurrencyConflict(string what, int expectedVersion) => new(
        $"the {what} was modified since the version this request expected", "concurrency_conflict", 409,
        new Dictionary<string, object?> { ["expected_version"] = expectedVersion });
}

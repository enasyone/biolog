namespace Biolog.Features.AttendanceSessions.Domain;

/// <summary>This feature's domain error, shaped identically to every sibling story's so it duck-types cleanly onto <see cref="Api.ProblemResponse"/>'s builder (ADR-0004 §2.1) - mirrors BIO-ATT-001's own <c>AttendanceError</c>.</summary>
public sealed class SessionError(string message, string code, int status, IReadOnlyDictionary<string, object?>? detail = null)
    : Exception(message)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
    public IReadOnlyDictionary<string, object?> Detail { get; } = detail ?? new Dictionary<string, object?>();

    public IReadOnlyList<Api.ValidationErrorItem>? Errors { get; init; }

    public static SessionError ActorRequired() => new(
        "X-Actor-Id is required to record who created or changed this session/policy/adjustment data", "actor_required", 400);

    public static SessionError InvalidActorType(string actorType) => new(
        "X-Actor-Type must be one of user, service_account, device, system, anonymous, unknown", "invalid_actor_type", 400,
        new Dictionary<string, object?> { ["actor_type"] = actorType });

    /// <summary>ADR-0006 Decision 3: required only on the policy-management and adjustment endpoints - see <see cref="IPermissionGateway"/>'s header.</summary>
    public static SessionError ActorProvenanceRequired() => new(
        "this action requires a valid X-Actor-Provenance proof for the asserted X-Actor-Id (ADR-0006 Decision 3) - a server-computed HMAC bound to that specific actor id, distinct from the caller-asserted X-Actor-Id itself",
        "actor_provenance_required", 401);

    public static SessionError Validation(IReadOnlyList<Api.ValidationErrorItem> errors) => new(
        "the request failed validation", "validation_failed", 422)
    { Errors = errors };

    public static SessionError AuditUnavailable() => new(
        "the audit sink failed; the change was not committed without an audit record", "audit_unavailable", 503);

    public static SessionError TenantContextMissing() => new(
        "tenant_context_missing: no tenant channel has resolved a context for this request", "tenant_context_missing", 400);

    public static SessionError PermissionRequired(string permissionKey) => new(
        $"this action requires the calling actor to hold the '{permissionKey}' permission", "permission_required", 403,
        new Dictionary<string, object?> { ["permission_key"] = permissionKey });

    public static SessionError PersonNotFound(string personId) => new(
        $"person not found in the resolved tenant: {personId}", "person_not_found", 404,
        new Dictionary<string, object?> { ["person_id"] = personId });

    public static SessionError SessionNotFound(string sessionId) => new(
        $"session not found in the resolved tenant: {sessionId}", "session_not_found", 404,
        new Dictionary<string, object?> { ["session_id"] = sessionId });

    /// <summary>AC04 - an adjustment may only target a person genuinely on that session's own roster.</summary>
    public static SessionError PersonNotOnRoster(string sessionId, string personId) => new(
        $"person '{personId}' is not on the roster of session '{sessionId}'", "person_not_on_roster", 422,
        new Dictionary<string, object?> { ["session_id"] = sessionId, ["person_id"] = personId });

    public static SessionError ConcurrencyConflict(string resource, int expectedVersion) => new(
        $"the {resource} was modified since the version this request expected", "concurrency_conflict", 409,
        new Dictionary<string, object?> { ["expected_version"] = expectedVersion });
}

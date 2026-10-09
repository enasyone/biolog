namespace Biolog.Features.Visitors.Domain;

/// <summary>This feature's domain error, shaped identically to every sibling story's so it duck-types cleanly onto <see cref="Api.ProblemResponse"/>'s builder (ADR-0004 S2.1) - mirrors BIO-ATT-001's own <c>AttendanceError</c>.</summary>
public sealed class VisitorError(string message, string code, int status, IReadOnlyDictionary<string, object?>? detail = null)
    : Exception(message)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
    public IReadOnlyDictionary<string, object?> Detail { get; } = detail ?? new Dictionary<string, object?>();

    public IReadOnlyList<Api.ValidationErrorItem>? Errors { get; init; }

    public static VisitorError ActorRequired() => new(
        "X-Actor-Id is required to record who preregistered or changed this visitor data", "actor_required", 400);

    public static VisitorError InvalidActorType(string actorType) => new(
        "X-Actor-Type must be one of user, service_account, device, system, anonymous, unknown", "invalid_actor_type", 400,
        new Dictionary<string, object?> { ["actor_type"] = actorType });

    /// <summary>ADR-0006 Decision 3: required only on the approve/deny and config-management endpoints - the authorization decisions this feature makes. See <see cref="IPermissionGateway"/>'s header.</summary>
    public static VisitorError ActorProvenanceRequired() => new(
        "this action requires a valid X-Actor-Provenance proof for the asserted X-Actor-Id (ADR-0006 Decision 3) - a server-computed HMAC bound to that specific actor id, distinct from the caller-asserted X-Actor-Id itself",
        "actor_provenance_required", 401);

    public static VisitorError Validation(IReadOnlyList<Api.ValidationErrorItem> errors) => new(
        "the request failed validation", "validation_failed", 422)
    { Errors = errors };

    public static VisitorError AuditUnavailable() => new(
        "the audit sink failed; the change was not committed without an audit record", "audit_unavailable", 503);

    public static VisitorError TenantContextMissing() => new(
        "tenant_context_missing: no tenant channel has resolved a context for this request", "tenant_context_missing", 400);

    public static VisitorError PermissionRequired(string permissionKey) => new(
        $"this action requires the calling actor to hold the '{permissionKey}' permission", "permission_required", 403,
        new Dictionary<string, object?> { ["permission_key"] = permissionKey });

    public static VisitorError PersonNotFound(string personId) => new(
        $"person not found in the resolved tenant: {personId}", "person_not_found", 404,
        new Dictionary<string, object?> { ["person_id"] = personId });

    public static VisitorError NotFound(string visitorId) => new(
        $"visitor record not found: {visitorId}", "visitor_not_found", 404,
        new Dictionary<string, object?> { ["visitor_id"] = visitorId });

    public static VisitorError ConcurrencyConflict(string visitorId, int expected, int actual) => new(
        $"visitor record '{visitorId}' was modified since the version this request expected", "concurrency_conflict", 409,
        new Dictionary<string, object?> { ["visitor_id"] = visitorId, ["expected_version"] = expected, ["actual_version"] = actual });

    /// <summary>AC02 - approve/deny only from <see cref="HostApprovalStatus.Pending"/>; no re-deciding an already-decided request.</summary>
    public static VisitorError ApprovalAlreadyDecided(string visitorId, HostApprovalStatus current) => new(
        $"visitor '{visitorId}' host approval was already decided ({current.ToWireString()}) - it cannot be re-decided", "approval_already_decided", 409,
        new Dictionary<string, object?> { ["visitor_id"] = visitorId, ["current_status"] = current.ToWireString() });

    public static VisitorError CheckInBlocked(string visitorId, string reasonCode, string message) => new(
        $"check-in blocked for visitor '{visitorId}': {message}", reasonCode, 409,
        new Dictionary<string, object?> { ["visitor_id"] = visitorId });

    public static VisitorError CheckOutBlocked(string visitorId, string message) => new(
        $"check-out blocked for visitor '{visitorId}': {message}", "not_checked_in", 409,
        new Dictionary<string, object?> { ["visitor_id"] = visitorId });

    public static VisitorError TemporaryCredentialBlocked(string visitorId, string message) => new(
        $"temporary credential cannot be issued for visitor '{visitorId}': {message}", "temporary_credential_blocked", 409,
        new Dictionary<string, object?> { ["visitor_id"] = visitorId });

    public static VisitorError ExpiresAtExceedsMax(int maxHours) => new(
        $"expires_at exceeds this tenant's configured maximum visit duration ({maxHours}h)", "expires_at_exceeds_max", 422,
        new Dictionary<string, object?> { ["max_visit_duration_hours"] = maxHours });
}

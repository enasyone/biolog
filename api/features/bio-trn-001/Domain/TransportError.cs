namespace Biolog.Features.Transport.Domain;

/// <summary>This feature's domain error, shaped identically to every sibling story's so it duck-types cleanly onto <see cref="Api.ProblemResponse"/>'s builder (ADR-0004 S2.1) - mirrors BIO-VIS-001's own <c>VisitorError</c>/BIO-ATT-001's own <c>AttendanceError</c>.</summary>
public sealed class TransportError(string message, string code, int status, IReadOnlyDictionary<string, object?>? detail = null)
    : Exception(message)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
    public IReadOnlyDictionary<string, object?> Detail { get; } = detail ?? new Dictionary<string, object?>();

    public IReadOnlyList<Api.ValidationErrorItem>? Errors { get; init; }

    public static TransportError ActorRequired() => new(
        "X-Actor-Id is required to record who made this change", "actor_required", 400);

    public static TransportError InvalidActorType(string actorType) => new(
        "X-Actor-Type must be one of user, service_account, device, system, anonymous, unknown", "invalid_actor_type", 400,
        new Dictionary<string, object?> { ["actor_type"] = actorType });

    /// <summary>ADR-0006 Decision 3: required only on the assign/end-assignment endpoints - the one authorization decision this feature makes. See <see cref="IPermissionGateway"/>'s header.</summary>
    public static TransportError ActorProvenanceRequired() => new(
        "this action requires a valid X-Actor-Provenance proof for the asserted X-Actor-Id (ADR-0006 Decision 3) - a server-computed HMAC bound to that specific actor id, distinct from the caller-asserted X-Actor-Id itself",
        "actor_provenance_required", 401);

    public static TransportError Validation(IReadOnlyList<Api.ValidationErrorItem> errors) => new(
        "the request failed validation", "validation_failed", 422)
    { Errors = errors };

    public static TransportError AuditUnavailable() => new(
        "the audit sink failed; the change was not committed without an audit record", "audit_unavailable", 503);

    public static TransportError TenantContextMissing() => new(
        "tenant_context_missing: no tenant channel has resolved a context for this request", "tenant_context_missing", 400);

    public static TransportError PermissionRequired(string permissionKey) => new(
        $"this action requires the calling actor to hold the '{permissionKey}' permission", "permission_required", 403,
        new Dictionary<string, object?> { ["permission_key"] = permissionKey });

    public static TransportError PersonNotFound(string personId) => new(
        $"person not found in the resolved tenant: {personId}", "person_not_found", 404,
        new Dictionary<string, object?> { ["person_id"] = personId });

    public static TransportError DeviceNotFound(string deviceId) => new(
        $"device not found: {deviceId}", "device_not_found", 404,
        new Dictionary<string, object?> { ["device_id"] = deviceId });

    public static TransportError RouteNotFound(string routeId) => new(
        $"route not found: {routeId}", "route_not_found", 404, new Dictionary<string, object?> { ["route_id"] = routeId });

    public static TransportError VehicleNotFound(string vehicleId) => new(
        $"vehicle not found: {vehicleId}", "vehicle_not_found", 404, new Dictionary<string, object?> { ["vehicle_id"] = vehicleId });

    public static TransportError StopNotFound(string stopId) => new(
        $"stop not found: {stopId}", "stop_not_found", 404, new Dictionary<string, object?> { ["stop_id"] = stopId });

    public static TransportError AssignmentNotFound(string assignmentId) => new(
        $"person assignment not found: {assignmentId}", "assignment_not_found", 404, new Dictionary<string, object?> { ["assignment_id"] = assignmentId });

    public static TransportError ConcurrencyConflict(string id, int expected, int actual) => new(
        $"record '{id}' was modified since the version this request expected", "concurrency_conflict", 409,
        new Dictionary<string, object?> { ["id"] = id, ["expected_version"] = expected, ["actual_version"] = actual });

    /// <summary>AC01 - one-way lifecycle: no un-retiring, no other illegal status transition.</summary>
    public static TransportError InvalidStatusTransition(string id, string from, string to) => new(
        $"'{id}' cannot move from {from} to {to} - lifecycle transitions are one-way and this one is not allowed", "invalid_status_transition", 409,
        new Dictionary<string, object?> { ["id"] = id, ["from"] = from, ["to"] = to });

    public static TransportError RouteNotActive(string routeId, string status) => new(
        $"route '{routeId}' is {status}, not active - a person may only be assigned to an active route", "route_not_active", 409,
        new Dictionary<string, object?> { ["route_id"] = routeId, ["status"] = status });

    public static TransportError StopNotActive(string stopId, string status) => new(
        $"stop '{stopId}' is {status}, not active", "stop_not_active", 409,
        new Dictionary<string, object?> { ["stop_id"] = stopId, ["status"] = status });

    public static TransportError StopNotOnRoute(string stopId, string routeId) => new(
        $"stop '{stopId}' is not one of route '{routeId}'s own stops", "stop_not_on_route", 422,
        new Dictionary<string, object?> { ["stop_id"] = stopId, ["route_id"] = routeId });

    /// <summary>AC04 - the one real campus-consistency rule this feature enforces (see docs/features/bio-trn-001/README.md "AC04: tenant/campus boundaries").</summary>
    public static TransportError CampusMismatch(string routeCampusId, string stopCampusId) => new(
        $"route campus '{routeCampusId}' does not match stop campus '{stopCampusId}' - a person cannot be assigned across a campus boundary", "campus_mismatch", 422,
        new Dictionary<string, object?> { ["route_campus_id"] = routeCampusId, ["stop_campus_id"] = stopCampusId });

    /// <summary>AC02 - at most one validity window per (tenant, person) may overlap another non-ended assignment's window.</summary>
    public static TransportError OverlappingAssignment(string personId, string conflictingAssignmentId) => new(
        $"person '{personId}' already has an overlapping assignment ({conflictingAssignmentId}) for this validity window", "overlapping_assignment", 409,
        new Dictionary<string, object?> { ["person_id"] = personId, ["conflicting_assignment_id"] = conflictingAssignmentId });

    public static TransportError AssignmentAlreadyEnded(string assignmentId) => new(
        $"assignment '{assignmentId}' has already ended - it cannot be ended again", "assignment_already_ended", 409,
        new Dictionary<string, object?> { ["assignment_id"] = assignmentId });
}

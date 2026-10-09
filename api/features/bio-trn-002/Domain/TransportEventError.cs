namespace Biolog.Features.TransportEvents.Domain;

/// <summary>This feature's domain error, shaped identically to every sibling story's so it duck-types cleanly onto <see cref="Api.ProblemResponse"/>'s builder (ADR-0004 S2.1).</summary>
public sealed class TransportEventError(string message, string code, int status, IReadOnlyDictionary<string, object?>? detail = null)
    : Exception(message)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
    public IReadOnlyDictionary<string, object?> Detail { get; } = detail ?? new Dictionary<string, object?>();

    public IReadOnlyList<Api.ValidationErrorItem>? Errors { get; init; }

    public static TransportEventError ActorRequired() => new(
        "X-Actor-Id is required to record who captured this transport event", "actor_required", 400);

    public static TransportEventError InvalidActorType(string actorType) => new(
        "X-Actor-Type must be one of user, service_account, device, system, anonymous, unknown", "invalid_actor_type", 400,
        new Dictionary<string, object?> { ["actor_type"] = actorType });

    public static TransportEventError Validation(IReadOnlyList<Api.ValidationErrorItem> errors) => new(
        "the request failed validation", "validation_failed", 422)
    { Errors = errors };

    public static TransportEventError AuditUnavailable() => new(
        "the audit sink failed; the change was not committed without an audit record", "audit_unavailable", 503);

    public static TransportEventError TenantContextMissing() => new(
        "tenant_context_missing: no tenant channel has resolved a context for this request", "tenant_context_missing", 400);

    public static TransportEventError PersonNotFound(string personId) => new(
        $"person not found in the resolved tenant: {personId}", "person_not_found", 404,
        new Dictionary<string, object?> { ["person_id"] = personId });

    public static TransportEventError RouteNotFound(string routeId) => new(
        $"route not found in the resolved tenant: {routeId}", "route_not_found", 404,
        new Dictionary<string, object?> { ["route_id"] = routeId });

    public static TransportEventError RouteNotActive(string routeId) => new(
        $"route {routeId} is not currently active", "route_not_active", 422,
        new Dictionary<string, object?> { ["route_id"] = routeId });

    public static TransportEventError VehicleNotActive(string vehicleId) => new(
        $"vehicle {vehicleId} is not currently active/in-service", "vehicle_not_active", 422,
        new Dictionary<string, object?> { ["vehicle_id"] = vehicleId });

    public static TransportEventError StopNotOnRoute(string stopId, string routeId) => new(
        $"stop {stopId} is not on route {routeId}", "stop_not_on_route", 422,
        new Dictionary<string, object?> { ["stop_id"] = stopId, ["route_id"] = routeId });

    public static TransportEventError StopNotActive(string stopId) => new(
        $"stop {stopId} is not currently active", "stop_not_active", 422,
        new Dictionary<string, object?> { ["stop_id"] = stopId });

    /// <summary>AC02 - the same idempotency key was reused with genuinely different content, mirrors BIO-ATT-001's own AC03 "event_id_reuse"-shaped conflict.</summary>
    public static TransportEventError IdempotencyConflict(string idempotencyKey) => new(
        $"idempotency_key '{idempotencyKey}' was already used to capture a different event - a producer must never reuse a key for different content", "idempotency_conflict", 409,
        new Dictionary<string, object?> { ["idempotency_key"] = idempotencyKey });

    public static TransportEventError NotificationConfigConcurrencyConflict(int expectedVersion) => new(
        "the notification config was modified since the version this request expected", "concurrency_conflict", 409,
        new Dictionary<string, object?> { ["expected_version"] = expectedVersion });
}

namespace Biolog.Features.PersonLifecycle.Domain;

/// <summary>
/// This feature's domain error, shaped identically to every sibling story's
/// (<c>PersonError</c>, <c>PersonProfileError</c>, <c>InstitutionalIdentifierError</c>) so it
/// duck-types cleanly onto <see cref="Api.ProblemResponse"/>'s builder (ADR-0004 §2.1).
/// </summary>
public sealed class PersonLifecycleError(string message, string code, int status, IReadOnlyDictionary<string, object?>? detail = null)
    : Exception(message)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
    public IReadOnlyDictionary<string, object?> Detail { get; } = detail ?? new Dictionary<string, object?>();

    public IReadOnlyList<Api.ValidationErrorItem>? Errors { get; init; }

    /// <summary>Mirrors BIO-IDN-002/BIO-IDN-003's identical "missing and foreign-tenant are indistinguishable" rule (ADR-0001 §1.5) - <see cref="Domain.IPersonLifecycleGateway.GetSnapshotAsync"/> returns null for both.</summary>
    public static PersonLifecycleError PersonNotFound(string personId) => new(
        $"person not found in the resolved tenant: {personId}", "person_not_found", 404,
        new Dictionary<string, object?> { ["person_id"] = personId });

    /// <summary>BIO-IDN-004-AC01's real enforcement point: <see cref="LifecycleTransitionPolicy"/> rejects this before the gateway is ever called.</summary>
    public static PersonLifecycleError TransitionNotAllowed(PersonStatus from, PersonStatus to) => new(
        $"the transition {from.ToWireString()} -> {to.ToWireString()} is not an allowed identity transition", "transition_not_allowed", 409,
        new Dictionary<string, object?> { ["from_status"] = from.ToWireString(), ["to_status"] = to.ToWireString() });

    public static PersonLifecycleError ConcurrencyConflict(string personId, int expected) => new(
        "the person was modified since the version this request expected", "concurrency_conflict", 409,
        new Dictionary<string, object?> { ["person_id"] = personId, ["expected_version"] = expected });

    /// <summary>BIO-IDN-004-AC03: reactivation (SUSPENDED -&gt; ACTIVE) requires the caller to assert this specific permission - the honest, disclosed pre-RBAC accommodation (matching every prior story's asserted-not-verified actor identity), pending BIO-IDN-006.</summary>
    public static PersonLifecycleError PermissionRequired(string requiredPermission) => new(
        $"reactivation requires the caller to assert the '{requiredPermission}' permission via X-Actor-Permission", "permission_required", 403,
        new Dictionary<string, object?> { ["required_permission"] = requiredPermission });

    /// <summary>BIO-IDN-004-AC03: reactivation requires a non-blank audit reason. Emergency suspension (AC02) does not require one - it is recorded when supplied, never mandatory.</summary>
    public static PersonLifecycleError ReasonRequired() => new(
        "reactivation requires a non-blank audit reason", "reason_required", 400);

    public static PersonLifecycleError ActorRequired() => new(
        "X-Actor-Id is required to record who made this administrative change", "actor_required", 400);

    public static PersonLifecycleError InvalidActorType(string actorType) => new(
        "X-Actor-Type must be one of user, service_account, device, system, anonymous, unknown", "invalid_actor_type", 400,
        new Dictionary<string, object?> { ["actor_type"] = actorType });

    public static PersonLifecycleError Validation(IReadOnlyList<Api.ValidationErrorItem> errors) => new(
        "the request failed validation", "validation_failed", 422)
    { Errors = errors };

    public static PersonLifecycleError AuditUnavailable() => new(
        "the underlying person status change committed, but this feature's own lifecycle-event record could not be confirmed written", "audit_unavailable", 503);

    /// <summary>
    /// BIO-IDN-004-QA-01 (Seyi round 3, High): the emergency suspension DID commit - the person
    /// is genuinely SUSPENDED (or, if reconciled later via <c>POST .../revocations/reconcile</c>,
    /// may since have moved on to any other status) - but the mandatory credential/access
    /// revocation event could not be confirmed written. This is an explicitly modelled,
    /// DURABLE, observable PENDING-RECONCILIATION fact
    /// (<see cref="PendingSuspensionReconciliation"/>), never a silent gap and never tied to the
    /// person's current status: <c>POST /api/v1/persons/{'{'}personId{'}'}/lifecycle/revocations/reconcile</c>
    /// closes it safely and idempotently, regardless of what the person's status has become
    /// since. See <see cref="PersonLifecycleService"/>'s header "Suspension/revocation
    /// atomicity" for the full three-round history of why this exists.
    /// </summary>
    public static PersonLifecycleError RevocationPendingReconciliation() => new(
        "the suspension committed, but the credential/access revocation event could not be confirmed written; POST .../lifecycle/revocations/reconcile to reconcile it",
        "revocation_pending_reconciliation", 503);

    public static PersonLifecycleError TenantContextMissing() => new(
        "tenant_context_missing: no tenant channel has resolved a context for this request", "tenant_context_missing", 400);

    /// <summary>Defence-in-depth: the gateway adapter reported an error code this project does not recognise - fails closed rather than silently succeeding.</summary>
    public static PersonLifecycleError UnrecognisedGatewayError(string errorCode) => new(
        $"the person lifecycle gateway reported an unrecognised error: {errorCode}", "gateway_error", 502,
        new Dictionary<string, object?> { ["gateway_error_code"] = errorCode });
}

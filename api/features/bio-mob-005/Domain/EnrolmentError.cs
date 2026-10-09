namespace Biolog.Features.OfflineEnrolment.Domain;

/// <summary>This feature's domain error, shaped identically to every sibling story's so it duck-types cleanly onto <see cref="Api.ProblemResponse"/>'s builder (ADR-0004 §2.1).</summary>
public sealed class EnrolmentError(string message, string code, int status, IReadOnlyDictionary<string, object?>? detail = null)
    : Exception(message)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
    public IReadOnlyDictionary<string, object?> Detail { get; } = detail ?? new Dictionary<string, object?>();

    public IReadOnlyList<Api.ValidationErrorItem>? Errors { get; init; }

    public static EnrolmentError Validation(IReadOnlyList<Api.ValidationErrorItem> errors) => new(
        "the request failed validation", "validation_failed", 422)
    { Errors = errors };

    public static EnrolmentError DeviceAuthenticationFailed() => new(
        "device authentication failed", "device_authentication_failed", 401);

    public static EnrolmentError IdempotencyKeyRequired() => new(
        "Idempotency-Key is required on this write endpoint (ADR-0001 §3.4)", "idempotency_key_required", 428);

    public static EnrolmentError IdempotencyKeyReuse() => new(
        "this Idempotency-Key was already used with different request content", "idempotency_key_reuse", 409);

    /// <summary>Idempotency-claim-race remediation (Tunde/Seyi/Zainab's finding, Kemi-confirmed High severity): a concurrent caller for the same Idempotency-Key is still in flight and did not complete within the bounded wait - the claim-loser waits bounded then fails closed, never silently executed a second time.</summary>
    public static EnrolmentError IdempotencyWaitTimeout(string idempotencyKey) => new(
        $"another request with idempotency key '{idempotencyKey}' is still in flight and did not complete within the wait window", "idempotency_wait_timeout", 503,
        new Dictionary<string, object?> { ["idempotency_key"] = idempotencyKey });

    public static EnrolmentError TenantMismatch(string claimedTenantId, string resolvedTenantId) => new(
        "the request body's tenant_id does not match the tenant resolved from the authenticated device",
        "tenant_mismatch", 403,
        new Dictionary<string, object?> { ["claimed_tenant_id"] = claimedTenantId, ["resolved_tenant_id"] = resolvedTenantId });

    public static EnrolmentError DeviceMismatch(string claimedDeviceId, string resolvedDeviceId) => new(
        "the request body's device_id does not match the authenticated device",
        "device_mismatch", 403,
        new Dictionary<string, object?> { ["claimed_device_id"] = claimedDeviceId, ["resolved_device_id"] = resolvedDeviceId });

    /// <summary>AC01, enforced server-side as defense-in-depth on top of the mobile UI's own policy-gated field visibility (Person.cs's own precedent: "validated at the request boundary AND the repository boundary").</summary>
    public static EnrolmentError FieldNotPermitted(string field) => new(
        "this field is not permitted by the tenant's offline capture policy", "field_not_permitted", 403,
        new Dictionary<string, object?> { ["field"] = field });

    public static EnrolmentError BiometricModalityNotPermitted(string modality) => new(
        "this biometric modality is not permitted by the tenant's offline capture policy", "biometric_modality_not_permitted", 403,
        new Dictionary<string, object?> { ["modality"] = modality });

    public static EnrolmentError PhotoCaptureNotPermitted() => new(
        "photo capture is not permitted by the tenant's offline capture policy", "photo_capture_not_permitted", 403);

    public static EnrolmentError AuditUnavailable() => new(
        "the audit sink failed; the change was not committed without an audit record", "audit_unavailable", 503);

    public static EnrolmentError ActorRequired() => new(
        "X-Actor-Id is required (ADR-0006 Decision 3, pending BIO-INT-001)", "actor_required", 401);

    public static EnrolmentError InvalidActorType(string value) => new(
        $"'{value}' is not a recognised actor type", "invalid_actor_type", 422);

    public static EnrolmentError ActorProvenanceRequired() => new(
        "X-Actor-Provenance is required and must verify against the resolved actor id (ADR-0006 Decision 3)",
        "actor_provenance_required", 401);

    public static EnrolmentError TenantContextMissing() => new(
        "no tenant could be resolved for this request (ADR-0002 Decision 2)", "tenant_context_missing", 403);

    public static EnrolmentError Forbidden(string permissionKey) => new(
        "the calling actor does not hold the required permission", "forbidden", 403,
        new Dictionary<string, object?> { ["permission_key"] = permissionKey });

    public static EnrolmentError PackageNotFound(string packageId) => new(
        "no enrolment package exists with this id in this tenant", "enrolment_package_not_found", 404,
        new Dictionary<string, object?> { ["package_id"] = packageId });

    public static EnrolmentError DuplicateCandidateNotFound(string candidateId) => new(
        "no duplicate candidate exists with this id in this tenant", "duplicate_candidate_not_found", 404,
        new Dictionary<string, object?> { ["candidate_id"] = candidateId });

    public static EnrolmentError DuplicateCandidateNotPending(string candidateId) => new(
        "this duplicate candidate has already been decided", "duplicate_candidate_not_pending", 409,
        new Dictionary<string, object?> { ["candidate_id"] = candidateId });

    public static EnrolmentError DecisionReasonRequired() => new(
        "a reason_code is required for a human duplicate-candidate decision (AC04)", "decision_reason_required", 422);
}

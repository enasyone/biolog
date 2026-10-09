namespace Biolog.Features.PhotoQuality.Domain;

/// <summary>
/// This feature's domain error, shaped identically to every sibling story's so it duck-types
/// cleanly onto <see cref="Api.ProblemResponse"/>'s builder (ADR-0004 §2.1).
/// </summary>
public sealed class PhotoQualityError(string message, string code, int status, IReadOnlyDictionary<string, object?>? detail = null)
    : Exception(message)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
    public IReadOnlyDictionary<string, object?> Detail { get; } = detail ?? new Dictionary<string, object?>();

    public IReadOnlyList<Api.ValidationErrorItem>? Errors { get; init; }

    public static PhotoQualityError TenantContextMissing() => new(
        "tenant_context_missing: no tenant channel has resolved a context for this request", "tenant_context_missing", 400);

    public static PhotoQualityError ActorRequired() => new(
        "X-Actor-Id is required to record who made this change", "actor_required", 400);

    /// <summary>ADR-0006 Decision 3 / ZB-PHO003-01 (Zainab, security round 1): thrown by this feature's own <c>ActorProvenanceGate</c> when the asserted actor id has no valid HMAC provenance proof.</summary>
    public static PhotoQualityError ActorProvenanceRequired() => new(
        "this action requires a valid X-Actor-Provenance proof for the asserted X-Actor-Id (ADR-0006 Decision 3)", "actor_provenance_required", 401);

    /// <summary>ZB-PHO003-01: thrown when a provenance-verified actor lacks the tenant-scoped grant a route requires.</summary>
    public static PhotoQualityError PermissionRequired(string permissionKey) => new(
        $"the resolved actor lacks the required permission: {permissionKey}", "permission_required", 403,
        new Dictionary<string, object?> { ["permission_key"] = permissionKey });

    /// <summary>ZB-PHO003-02: an approval attempt is already in progress for this capture (its durable marker is set) - rejecting now could orphan a real BIO-PHO-001 PhotoMaster version an earlier, interrupted attempt already promoted. The caller must retry Approve (which resumes and finalizes), never Reject.</summary>
    public static PhotoQualityError ApprovalAlreadyInProgress(string personId, int sequenceNumber) => new(
        $"an approval attempt for {personId}/{sequenceNumber} is already in progress - retry approve to resume and finalize, this capture can no longer be rejected", "approval_already_in_progress", 409,
        new Dictionary<string, object?> { ["person_id"] = personId, ["sequence_number"] = sequenceNumber });

    /// <summary>ZB-PHO003-04 (Kemi's ruling): a concurrent attempt to approve this exact capture (same attempt id) is still in flight and did not complete within the bounded wait - ADR-0004/BIO-ARC-004's documented "claim-loser waits bounded then idempotency_wait_timeout (503)" behavior, same shape BIO-PHO-002's own TD-PHO-002-IDEMPOTENCY-RACE remediation already uses, never silently executed a second time.</summary>
    public static PhotoQualityError IdempotencyWaitTimeout(string attemptId) => new(
        $"another attempt to approve this capture (attempt id '{attemptId}') is still in flight and did not complete within the wait window", "idempotency_wait_timeout", 503,
        new Dictionary<string, object?> { ["attempt_id"] = attemptId });

    public static PhotoQualityError InvalidActorType(string actorType) => new(
        "X-Actor-Type must be one of user, service_account, device, system, anonymous, unknown", "invalid_actor_type", 400,
        new Dictionary<string, object?> { ["actor_type"] = actorType });

    public static PhotoQualityError Validation(IReadOnlyList<Api.ValidationErrorItem> errors) => new(
        "the request failed validation", "validation_failed", 422)
    { Errors = errors };

    public static PhotoQualityError AuditUnavailable() => new(
        "the audit sink failed; the change was not committed without an audit record", "audit_unavailable", 503);

    public static PhotoQualityError UnsupportedFormat() => new(
        "only PNG and JPEG photos are supported", "unsupported_format", 422);

    /// <summary>Applying BIO-IDN-002-QA-01's lesson from the start - see IPersonExistenceGateway's header. Deliberately the same code/status/shape as a foreign-tenant person (ADR-0001 §1.5, no enumeration oracle).</summary>
    public static PhotoQualityError PersonNotFound(string personId) => new(
        $"person not found in the resolved tenant: {personId}", "person_not_found", 404,
        new Dictionary<string, object?> { ["person_id"] = personId });

    public static PhotoQualityError CaptureNotFound(string personId, int sequenceNumber) => new(
        $"capture not found: {personId}/{sequenceNumber}", "capture_not_found", 404,
        new Dictionary<string, object?> { ["person_id"] = personId, ["sequence_number"] = sequenceNumber });

    public static PhotoQualityError CaptureConcurrencyConflict(string personId, int sequenceNumber, int expected, int actual) => new(
        "the capture was modified since the version this request expected", "concurrency_conflict", 409,
        new Dictionary<string, object?>
        {
            ["person_id"] = personId, ["sequence_number"] = sequenceNumber, ["expected_version"] = expected, ["actual_version"] = actual,
        });

    /// <summary>BIO-PHO-003-AC03's core enforcement point: only a PENDING capture may be approved or rejected - a capture already decided is terminal.</summary>
    public static PhotoQualityError CaptureNotPending(string personId, int sequenceNumber, Domain.PhotoQualityCaptureStatus status) => new(
        $"only a PENDING capture may be approved or rejected: {personId}/{sequenceNumber} is {status}", "capture_not_pending", 409,
        new Dictionary<string, object?> { ["person_id"] = personId, ["sequence_number"] = sequenceNumber, ["status"] = status.ToString() });
}

namespace Biolog.Features.PhotoBooth.Domain;

/// <summary>
/// This feature's domain error, shaped identically to every sibling story's so it duck-types
/// cleanly onto <see cref="Api.ProblemResponse"/>'s builder (ADR-0004 §2.1).
/// </summary>
public sealed class PhotoBoothError(string message, string code, int status, IReadOnlyDictionary<string, object?>? detail = null)
    : Exception(message)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
    public IReadOnlyDictionary<string, object?> Detail { get; } = detail ?? new Dictionary<string, object?>();

    public IReadOnlyList<Api.ValidationErrorItem>? Errors { get; init; }

    public static PhotoBoothError TenantContextMissing() => new(
        "tenant_context_missing: no tenant channel has resolved a context for this request", "tenant_context_missing", 400);

    public static PhotoBoothError ActorRequired() => new(
        "X-Actor-Id is required to record who made this change", "actor_required", 400);

    public static PhotoBoothError InvalidActorType(string actorType) => new(
        "X-Actor-Type must be one of user, service_account, device, system, anonymous, unknown", "invalid_actor_type", 400,
        new Dictionary<string, object?> { ["actor_type"] = actorType });

    public static PhotoBoothError Validation(IReadOnlyList<Api.ValidationErrorItem> errors) => new(
        "the request failed validation", "validation_failed", 422)
    { Errors = errors };

    public static PhotoBoothError AuditUnavailable() => new(
        "the audit sink failed; the change was not committed without an audit record", "audit_unavailable", 503);

    public static PhotoBoothError UnsupportedFormat() => new(
        "only PNG and JPEG captures are supported", "unsupported_format", 422);

    /// <summary>BIO-PHO-002-AC02: identical for a missing device, an inactive device, and a wrong secret - a caller cannot use this endpoint to enumerate which device ids are registered or active.</summary>
    public static PhotoBoothError DeviceAuthenticationFailed() => new(
        "device authentication failed", "device_authentication_failed", 401);

    /// <summary>Applying BIO-IDN-002-QA-01's lesson from the start - see IPersonExistenceGateway's header. Deliberately the same code/status/shape as a foreign-tenant person (ADR-0001 §1.5, no enumeration oracle).</summary>
    public static PhotoBoothError PersonNotFound(string personId) => new(
        $"person not found in the resolved tenant: {personId}", "person_not_found", 404,
        new Dictionary<string, object?> { ["person_id"] = personId });

    public static PhotoBoothError IdempotencyKeyRequired() => new(
        "Idempotency-Key is required on this write endpoint (ADR-0001 §3.4)", "idempotency_key_required", 428);

    public static PhotoBoothError IdempotencyKeyReuse() => new(
        "this Idempotency-Key was already used with different request content", "idempotency_key_reuse", 409);

    /// <summary>TD-PHO-002-IDEMPOTENCY-RACE's own remediation: a concurrent caller for the same Idempotency-Key is still in flight and did not complete within the bounded wait - ADR-0004/BIO-ARC-004's documented "claim-loser waits bounded then idempotency_wait_timeout (503)" behavior, never silently executed a second time.</summary>
    public static PhotoBoothError IdempotencyWaitTimeout(string idempotencyKey) => new(
        $"another request with idempotency key '{idempotencyKey}' is still in flight and did not complete within the wait window", "idempotency_wait_timeout", 503,
        new Dictionary<string, object?> { ["idempotency_key"] = idempotencyKey });

    public static PhotoBoothError CaptureNotFound(string personId, int sequenceNumber) => new(
        $"capture not found: {personId}/{sequenceNumber}", "capture_not_found", 404,
        new Dictionary<string, object?> { ["person_id"] = personId, ["sequence_number"] = sequenceNumber });

    public static PhotoBoothError CaptureConcurrencyConflict(string personId, int sequenceNumber, int expected, int actual) => new(
        "the capture was modified since the version this request expected", "concurrency_conflict", 409,
        new Dictionary<string, object?>
        {
            ["person_id"] = personId, ["sequence_number"] = sequenceNumber, ["expected_version"] = expected, ["actual_version"] = actual,
        });

    /// <summary>BIO-PHO-002-AC04's core enforcement point: only a PENDING capture may be approved or rejected - a capture that was auto-rejected at submission, or already decided, is terminal.</summary>
    public static PhotoBoothError CaptureNotPending(string personId, int sequenceNumber, Domain.CaptureStatus status) => new(
        $"only a PENDING capture may be approved or rejected: {personId}/{sequenceNumber} is {status}", "capture_not_pending", 409,
        new Dictionary<string, object?> { ["person_id"] = personId, ["sequence_number"] = sequenceNumber, ["status"] = status.ToString() });
}

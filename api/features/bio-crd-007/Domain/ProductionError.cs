namespace Biolog.Features.CardProduction.Domain;

/// <summary>This feature's domain error, shaped identically to every sibling story's so it duck-types cleanly onto <see cref="Api.ProblemResponse"/>'s builder (ADR-0004 §2.1).</summary>
public sealed class ProductionError(string message, string code, int status, IReadOnlyDictionary<string, object?>? detail = null)
    : Exception(message)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
    public IReadOnlyDictionary<string, object?> Detail { get; } = detail ?? new Dictionary<string, object?>();

    public IReadOnlyList<Api.ValidationErrorItem>? Errors { get; init; }

    public static ProductionError CardIssueNotFound(string personId, string cardClass, int sequenceNumber) => new(
        $"no card issue found for person {personId}, card class {cardClass}, sequence {sequenceNumber}", "card_issue_not_found", 404,
        new Dictionary<string, object?> { ["person_id"] = personId, ["card_class"] = cardClass, ["sequence_number"] = sequenceNumber });

    public static ProductionError JobNotFound(string personId, string cardClass, int sequenceNumber, int jobSequenceNumber) => new(
        $"no production job {jobSequenceNumber} found for person {personId}, card class {cardClass}, sequence {sequenceNumber}", "production_job_not_found", 404,
        new Dictionary<string, object?> { ["person_id"] = personId, ["card_class"] = cardClass, ["sequence_number"] = sequenceNumber, ["job_sequence_number"] = jobSequenceNumber });

    public static ProductionError PreconditionRequired() => new(
        "expected_version is required on every mutating write (ADR-0001 §3.2)", "precondition_required", 428);

    public static ProductionError ConcurrencyConflict(int expected, int actual) => new(
        "the production job was modified since the version this request expected", "concurrency_conflict", 409,
        new Dictionary<string, object?> { ["expected_version"] = expected, ["actual_version"] = actual });

    /// <summary>A step/reject/cancel attempted on a job that has already resolved (Passed/Failed/Rejected/Cancelled).</summary>
    public static ProductionError JobNotOpen(string action, ProductionJobStatus status) => new(
        $"cannot {action} a production job that is already {status.ToWireString()}", "production_job_not_open", 409,
        new Dictionary<string, object?> { ["action"] = action, ["status"] = status.ToWireString() });

    /// <summary>A step (print/encode/read-back-QC) attempted a second time on the same job - a genuine re-attempt is a new job (reprint), never a correction to this one.</summary>
    public static ProductionError StepAlreadyRecorded(string step) => new(
        $"{step} was already recorded for this production job and cannot be recorded again", "production_step_already_recorded", 409,
        new Dictionary<string, object?> { ["step"] = step });

    /// <summary>BIO-CRD-007-AC04: a reprint (job sequence &gt; 1) must record why; a first attempt must not.</summary>
    public static ProductionError ReprintReasonRequired() => new(
        "reprint_reason is required when this is not the first production attempt for this card issue", "reprint_reason_required", 422);

    public static ProductionError ReprintReasonNotAllowed() => new(
        "reprint_reason is only meaningful on a reprint (job_sequence_number > 1)", "reprint_reason_not_allowed", 422);

    /// <summary>BIO-CRD-007-AC02: activation refused because no production job for this card has ever reached Passed.</summary>
    public static ProductionError ProductionNotPassed(int? latestJobSequenceNumber, ProductionJobStatus? latestStatus) => new(
        "activation refused: this card issue has no production job that has passed print/encode/read-back-QC", "production_not_passed", 409,
        new Dictionary<string, object?> { ["latest_job_sequence_number"] = latestJobSequenceNumber, ["latest_status"] = latestStatus?.ToWireString() });

    public static ProductionError Validation(IReadOnlyList<Api.ValidationErrorItem> errors) => new(
        "the request failed validation", "validation_failed", 422)
    { Errors = errors };

    public static ProductionError AuditUnavailable() => new(
        "the audit sink failed; the change was not committed without an audit record", "audit_unavailable", 503);

    /// <summary>ADR-0001 §3.4 / ADR-0004 §4.3: card activation and offline sync intake are named required-idempotency-key operations.</summary>
    public static ProductionError IdempotencyKeyRequired() => new(
        "Idempotency-Key is required on this write endpoint (ADR-0001 §3.4)", "idempotency_key_required", 428);

    public static ProductionError IdempotencyKeyReuse() => new(
        "this Idempotency-Key was already used with different request content", "idempotency_key_reuse", 409);

    /// <summary>TD-CRD-007-IDEMPOTENCY-RACE's own remediation: a concurrent caller for the same Idempotency-Key is still in flight and did not complete within the bounded wait - ADR-0004/BIO-ARC-004's documented "claim-loser waits bounded then idempotency_wait_timeout (503)" behavior, never silently executed a second time.</summary>
    public static ProductionError IdempotencyWaitTimeout(string idempotencyKey) => new(
        $"another request with idempotency key '{idempotencyKey}' is still in flight and did not complete within the wait window", "idempotency_wait_timeout", 503,
        new Dictionary<string, object?> { ["idempotency_key"] = idempotencyKey });

    public static ProductionError ActorRequired() => new(
        "X-Actor-Id is required to record who performed this production action", "actor_required", 400);

    public static ProductionError InvalidActorType(string actorType) => new(
        "X-Actor-Type must be one of user, service_account, device, system, anonymous, unknown", "invalid_actor_type", 400,
        new Dictionary<string, object?> { ["actor_type"] = actorType });

    public static ProductionError TenantContextMissing() => new(
        "tenant_context_missing: no tenant channel has resolved a context for this request", "tenant_context_missing", 400);

    // --- AC03: signed, time-limited production packages ---

    public static ProductionError PackageNotFound(string packageId) => new(
        $"no production package found for id '{packageId}'", "production_package_not_found", 404,
        new Dictionary<string, object?> { ["package_id"] = packageId });

    public static ProductionError PackageSignatureInvalid() => new(
        "the production package's signature does not match - it was not issued by this tenant, or has been tampered with", "production_package_signature_invalid", 401);

    public static ProductionError PackageExpired(string expiresAt) => new(
        $"the production package expired at {expiresAt}", "production_package_expired", 401,
        new Dictionary<string, object?> { ["expires_at"] = expiresAt });

    public static ProductionError PackageAlreadyConsumed(string packageId) => new(
        $"production package '{packageId}' has already been used to report a completed job", "production_package_already_consumed", 409,
        new Dictionary<string, object?> { ["package_id"] = packageId });

    public static ProductionError PackageTenantMismatch() => new(
        "the production package was not issued for the resolved tenant", "production_package_tenant_mismatch", 401);
}

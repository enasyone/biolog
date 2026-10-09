namespace Biolog.Features.CardLifecycle.Domain;

/// <summary>This feature's domain error, shaped identically to every sibling story's so it duck-types cleanly onto <see cref="Api.ProblemResponse"/>'s builder (ADR-0004 §2.1).</summary>
public sealed class CardLifecycleError(string message, string code, int status, IReadOnlyDictionary<string, object?>? detail = null)
    : Exception(message)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
    public IReadOnlyDictionary<string, object?> Detail { get; } = detail ?? new Dictionary<string, object?>();

    public IReadOnlyList<Api.ValidationErrorItem>? Errors { get; init; }

    public static CardLifecycleError CardIssueNotFound(string personId, string cardClass, int sequenceNumber) => new(
        $"no card issue found for person {personId}, card class {cardClass}, sequence {sequenceNumber}", "card_issue_not_found", 404,
        new Dictionary<string, object?> { ["person_id"] = personId, ["card_class"] = cardClass, ["sequence_number"] = sequenceNumber });

    public static CardLifecycleError RecordNotFound(string personId, string cardClass, int sequenceNumber) => new(
        $"no card lifecycle record found for person {personId}, card class {cardClass}, sequence {sequenceNumber}", "card_lifecycle_record_not_found", 404,
        new Dictionary<string, object?> { ["person_id"] = personId, ["card_class"] = cardClass, ["sequence_number"] = sequenceNumber });

    /// <summary>ADR-0001 §3.2: expected_version absent on a mutating write to an EXISTING record. A first-ever report needs none, the same "no CAS check on first creation" precedent BIO-CRD-005's own IssueAsync established.</summary>
    public static CardLifecycleError PreconditionRequired() => new(
        "expected_version is required on every mutating write to an existing card lifecycle record (ADR-0001 §3.2)", "precondition_required", 428);

    public static CardLifecycleError ConcurrencyConflict(int expected, int actual) => new(
        "the card lifecycle record was modified since the version this request expected", "concurrency_conflict", 409,
        new Dictionary<string, object?> { ["expected_version"] = expected, ["actual_version"] = actual });

    /// <summary>BIO-CRD-006-AC01: a card already Locked cannot be reported again without first being recovered or superseded.</summary>
    public static CardLifecycleError AlreadyReported(string personId, string cardClass, int sequenceNumber) => new(
        $"card {sequenceNumber} for person {personId}/{cardClass} is already reported and locked", "card_already_reported", 409,
        new Dictionary<string, object?> { ["person_id"] = personId, ["card_class"] = cardClass, ["sequence_number"] = sequenceNumber });

    /// <summary>A report/recover attempted from a lock state that does not allow it (e.g. recovering a card that was never reported, or one already permanently revoked).</summary>
    public static CardLifecycleError InvalidTransition(string action, CardLockState from) => new(
        $"cannot {action} a card lifecycle record currently {from.ToWireString()}", "invalid_card_lifecycle_transition", 409,
        new Dictionary<string, object?> { ["action"] = action, ["from_state"] = from.ToWireString() });

    /// <summary>BIO-CRD-006-AC02: recovery is refused by tenant policy for this reason code.</summary>
    public static CardLifecycleError RecoveryNotPermitted(string reasonCode) => new(
        $"recovery is not permitted by tenant policy for reason code '{reasonCode}'", "recovery_not_permitted", 409,
        new Dictionary<string, object?> { ["reason_code"] = reasonCode });

    /// <summary>BIO-CRD-006-QA-02: the replacement itself is not in a state BIO-CRD-005's own ActivateAsync would ever accept (e.g. already Deactivated) - rejected before any earlier-card revocation is attempted.</summary>
    public static CardLifecycleError ReplacementNotActivatable(int sequenceNumber, string status) => new(
        $"replacement sequence {sequenceNumber} cannot be activated from its current status '{status}'", "replacement_not_activatable", 409,
        new Dictionary<string, object?> { ["sequence_number"] = sequenceNumber, ["status"] = status });

    /// <summary>BIO-OPS-007-AC01 (Femi's concurrent-replacement hardening find): a later-sequence sibling already exists and has not been Deactivated, so this strictly-earlier sequence can never become the active card - see <see cref="CardLifecycleService.ActivateReplacementAsync"/>'s own header for why only the highest live sequence number may ever activate.</summary>
    public static CardLifecycleError SupersededByLaterReplacement(int sequenceNumber, int latestSequenceNumber) => new(
        $"replacement sequence {sequenceNumber} cannot be activated: a later replacement (sequence {latestSequenceNumber}) for this person/card class already exists and is not deactivated", "superseded_by_later_replacement", 409,
        new Dictionary<string, object?> { ["sequence_number"] = sequenceNumber, ["latest_sequence_number"] = latestSequenceNumber });

    /// <summary>BIO-OPS-003-AC04: activation of a replacement is refused unless BIO-CRD-007's own latest production job for this card has Passed - see <see cref="IProductionStatusGateway"/>'s header for the composition gap this closes.</summary>
    public static CardLifecycleError ProductionNotPassed(int? latestJobSequenceNumber, ProductionJobLifecycleStatus? latestStatus) => new(
        "activation refused: this card issue has no production job that has passed print/encode/read-back-QC", "production_not_passed", 409,
        new Dictionary<string, object?> { ["latest_job_sequence_number"] = latestJobSequenceNumber, ["latest_status"] = latestStatus?.ToString().ToLowerInvariant() });

    public static CardLifecycleError Validation(IReadOnlyList<Api.ValidationErrorItem> errors) => new(
        "the request failed validation", "validation_failed", 422)
    { Errors = errors };

    public static CardLifecycleError AuditUnavailable() => new(
        "the audit sink failed; the change was not committed without an audit record", "audit_unavailable", 503);

    /// <summary>ADR-0001 §3.4 / ADR-0004 §4.3: activate-replacement performs a card activation, and presentation-event intake is this story's own "offline sync intake" - both named required-idempotency-key operations.</summary>
    public static CardLifecycleError IdempotencyKeyRequired() => new(
        "Idempotency-Key is required on this write endpoint (ADR-0001 §3.4)", "idempotency_key_required", 428);

    public static CardLifecycleError IdempotencyKeyReuse() => new(
        "this Idempotency-Key was already used with different request content", "idempotency_key_reuse", 409);

    /// <summary>TD-CRD-006-IDEMPOTENCY-RACE's own remediation: a concurrent caller for the same Idempotency-Key is still in flight and did not complete within the bounded wait - ADR-0004/BIO-ARC-004's documented "claim-loser waits bounded then idempotency_wait_timeout (503)" behavior, never silently executed a second time.</summary>
    public static CardLifecycleError IdempotencyWaitTimeout(string idempotencyKey) => new(
        $"another request with idempotency key '{idempotencyKey}' is still in flight and did not complete within the wait window", "idempotency_wait_timeout", 503,
        new Dictionary<string, object?> { ["idempotency_key"] = idempotencyKey });

    public static CardLifecycleError ActorRequired() => new(
        "X-Actor-Id is required to record who performed this card-lifecycle action", "actor_required", 400);

    public static CardLifecycleError InvalidActorType(string actorType) => new(
        "X-Actor-Type must be one of user, service_account, device, system, anonymous, unknown", "invalid_actor_type", 400,
        new Dictionary<string, object?> { ["actor_type"] = actorType });

    public static CardLifecycleError TenantContextMissing() => new(
        "tenant_context_missing: no tenant channel has resolved a context for this request", "tenant_context_missing", 400);

    /// <summary>TD-CRD-006-DENY-LIST-DEVICE-AUTH: identical for every failure mode (no id, no secret, malformed id, unregistered device, inactive device, wrong secret) so a caller cannot enumerate device/credential state through this endpoint's own error shape.</summary>
    public static CardLifecycleError DeviceAuthenticationFailed() => new(
        "device authentication failed", "device_authentication_failed", 401);
}

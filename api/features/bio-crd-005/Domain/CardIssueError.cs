namespace Biolog.Features.CardIssuance.Domain;

/// <summary>This feature's domain error, shaped identically to every sibling story's so it duck-types cleanly onto <see cref="Api.ProblemResponse"/>'s builder (ADR-0004 §2.1).</summary>
public sealed class CardIssueError(string message, string code, int status, IReadOnlyDictionary<string, object?>? detail = null)
    : Exception(message)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
    public IReadOnlyDictionary<string, object?> Detail { get; } = detail ?? new Dictionary<string, object?>();

    public IReadOnlyList<Api.ValidationErrorItem>? Errors { get; init; }

    public static CardIssueError PersonNotFound(string personId) => new(
        $"person not found: {personId}", "person_not_found", 404,
        new Dictionary<string, object?> { ["person_id"] = personId });

    public static CardIssueError CardIssueNotFound(string personId, string cardClass, int sequenceNumber) => new(
        $"no card issue found for person {personId}, card class {cardClass}, sequence {sequenceNumber}", "card_issue_not_found", 404,
        new Dictionary<string, object?> { ["person_id"] = personId, ["card_class"] = cardClass, ["sequence_number"] = sequenceNumber });

    /// <summary>ADR-0001 §3.2: expected_version absent on a mutating write.</summary>
    public static CardIssueError PreconditionRequired() => new(
        "expected_version is required on every mutating write (ADR-0001 §3.2)", "precondition_required", 428);

    /// <summary>ADR-0001 §3.2: expected_version present but stale.</summary>
    public static CardIssueError ConcurrencyConflict(int expected, int actual) => new(
        "the card issue record was modified since the version this request expected", "concurrency_conflict", 409,
        new Dictionary<string, object?> { ["expected_version"] = expected, ["actual_version"] = actual });

    /// <summary>BIO-CRD-005-AC04: the core invariant - activation is refused, never silently superseding, when another card for the same (person, card class) is already active.</summary>
    public static CardIssueError AnotherCardAlreadyActive(string personId, string cardClass, int activeSequenceNumber) => new(
        $"person {personId} already has an active card in class '{cardClass}' (sequence {activeSequenceNumber}) - deactivate it first",
        "another_card_already_active", 409,
        new Dictionary<string, object?> { ["person_id"] = personId, ["card_class"] = cardClass, ["active_sequence_number"] = activeSequenceNumber });

    /// <summary>A step (encode/print/verify/activate/deactivate) attempted from a status that does not allow it.</summary>
    public static CardIssueError InvalidTransition(string action, CardIssueStatus from) => new(
        $"cannot {action} a card issue currently {from.ToWireString()}", "invalid_card_issue_transition", 409,
        new Dictionary<string, object?> { ["action"] = action, ["from_status"] = from.ToWireString() });

    /// <summary>A step (encode/print/verify) attempted a second time - re-doing a physical step means a new card issue, not overwriting this one's provenance.</summary>
    public static CardIssueError StepAlreadyRecorded(string step) => new(
        $"{step} was already recorded for this card issue and cannot be recorded again", "card_issue_step_already_recorded", 409,
        new Dictionary<string, object?> { ["step"] = step });

    public static CardIssueError Validation(IReadOnlyList<Api.ValidationErrorItem> errors) => new(
        "the request failed validation", "validation_failed", 422)
    { Errors = errors };

    public static CardIssueError AuditUnavailable() => new(
        "the audit sink failed; the change was not committed without an audit record", "audit_unavailable", 503);

    /// <summary>ADR-0001 §3.4 / ADR-0004 §4.3: card activation is a named required-idempotency-key operation.</summary>
    public static CardIssueError IdempotencyKeyRequired() => new(
        "Idempotency-Key is required on this write endpoint (ADR-0001 §3.4)", "idempotency_key_required", 428);

    public static CardIssueError IdempotencyKeyReuse() => new(
        "this Idempotency-Key was already used with different request content", "idempotency_key_reuse", 409);

    /// <summary>TD-CRD-005-IDEMPOTENCY-RACE's own remediation: a concurrent caller for the same Idempotency-Key is still in flight and did not complete within the bounded wait - ADR-0004/BIO-ARC-004's documented "claim-loser waits bounded then idempotency_wait_timeout (503)" behavior, never silently executed a second time.</summary>
    public static CardIssueError IdempotencyWaitTimeout(string idempotencyKey) => new(
        $"another request with idempotency key '{idempotencyKey}' is still in flight and did not complete within the wait window", "idempotency_wait_timeout", 503,
        new Dictionary<string, object?> { ["idempotency_key"] = idempotencyKey });

    public static CardIssueError ActorRequired() => new(
        "X-Actor-Id is required to record who performed this card-issue action", "actor_required", 400);

    public static CardIssueError InvalidActorType(string actorType) => new(
        "X-Actor-Type must be one of user, service_account, device, system, anonymous, unknown", "invalid_actor_type", 400,
        new Dictionary<string, object?> { ["actor_type"] = actorType });

    public static CardIssueError TenantContextMissing() => new(
        "tenant_context_missing: no tenant channel has resolved a context for this request", "tenant_context_missing", 400);
}

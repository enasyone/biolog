namespace Biolog.Features.AccessDecisions.Domain;

/// <summary>This feature's domain error, shaped identically to every sibling story's so it duck-types cleanly onto <see cref="Api.ProblemResponse"/>'s builder (ADR-0004 S2.1).</summary>
public sealed class AccessDecisionError(string message, string code, int status, IReadOnlyDictionary<string, object?>? detail = null)
    : Exception(message)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
    public IReadOnlyDictionary<string, object?> Detail { get; } = detail ?? new Dictionary<string, object?>();

    public IReadOnlyList<Api.ValidationErrorItem>? Errors { get; init; }

    public static AccessDecisionError ActorRequired() => new(
        "X-Actor-Id is required to attribute who requested this access decision", "actor_required", 400);

    public static AccessDecisionError InvalidActorType(string actorType) => new(
        "X-Actor-Type must be one of user, service_account, device, system, anonymous, unknown", "invalid_actor_type", 400,
        new Dictionary<string, object?> { ["actor_type"] = actorType });

    /// <summary>ADR-0006 Decision 3: this feature's own decision endpoint reaches the SAME genuine authorization decision BIO-ACC-001's own gated /evaluate route reaches, so it carries the identical provenance requirement.</summary>
    public static AccessDecisionError ActorProvenanceRequired() => new(
        "a verified X-Actor-Provenance proof for the asserted actor is required to request an access decision", "actor_provenance_required", 401);

    public static AccessDecisionError Validation(IReadOnlyList<Api.ValidationErrorItem> errors) => new(
        "the request failed validation", "validation_failed", 422)
    { Errors = errors };

    public static AccessDecisionError AuditUnavailable() => new(
        "the audit sink failed; the decision was not committed without an audit record", "audit_unavailable", 503);

    public static AccessDecisionError TenantContextMissing() => new(
        "tenant_context_missing: no tenant channel has resolved a context for this request", "tenant_context_missing", 400);

    public static AccessDecisionError PersonNotFound(string personId) => new(
        $"person not found in the resolved tenant: {personId}", "person_not_found", 404,
        new Dictionary<string, object?> { ["person_id"] = personId });

    /// <summary>AC02/AC03 - the same idempotency key was reused with genuinely different request content, mirrors BIO-ATT-001's/BIO-TRN-002's own idempotency-conflict shape.</summary>
    public static AccessDecisionError IdempotencyConflict(string idempotencyKey) => new(
        $"idempotency_key '{idempotencyKey}' was already used to compute a different decision - a caller must never reuse a key for different content", "idempotency_conflict", 409,
        new Dictionary<string, object?> { ["idempotency_key"] = idempotencyKey });
}

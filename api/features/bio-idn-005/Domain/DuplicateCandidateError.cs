namespace Biolog.Features.DuplicateReview.Domain;

/// <summary>
/// This feature's domain error, shaped identically to every sibling story's
/// (<c>PersonError</c>, <c>PersonProfileError</c>, <c>InstitutionalIdentifierError</c>) so it
/// duck-types cleanly onto <see cref="Api.ProblemResponse"/>'s builder (ADR-0004 §2.1).
/// </summary>
public sealed class DuplicateCandidateError(string message, string code, int status, IReadOnlyDictionary<string, object?>? detail = null)
    : Exception(message)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
    public IReadOnlyDictionary<string, object?> Detail { get; } = detail ?? new Dictionary<string, object?>();

    public IReadOnlyList<Api.ValidationErrorItem>? Errors { get; init; }

    /// <summary>
    /// Applying BIO-IDN-002-QA-01's lesson from the start: the resolved-tenant PERSON a scan or
    /// decision targets does not exist - either genuinely missing, or existing only in a
    /// different tenant. Deliberately the SAME code/status/shape for both causes (ADR-0001
    /// §1.5: a canonical identifier carries no authority; distinguishing "missing" from "exists
    /// in another tenant" here would be a cross-tenant enumeration oracle - BIO-IDN-005-AC04).
    /// </summary>
    public static DuplicateCandidateError PersonNotFound(string personId) => new(
        $"person not found in the resolved tenant: {personId}", "person_not_found", 404,
        new Dictionary<string, object?> { ["person_id"] = personId });

    public static DuplicateCandidateError NotFound(string tenantId, string personIdLow, string personIdHigh) => new(
        $"duplicate candidate not found: {personIdLow}/{personIdHigh}", "duplicate_candidate_not_found", 404,
        new Dictionary<string, object?> { ["person_id_low"] = personIdLow, ["person_id_high"] = personIdHigh });

    public static DuplicateCandidateError ConcurrencyConflict(string personIdLow, string personIdHigh, int expected, int actual) => new(
        "the duplicate candidate was modified since the version this request expected", "concurrency_conflict", 409,
        new Dictionary<string, object?>
        {
            ["person_id_low"] = personIdLow, ["person_id_high"] = personIdHigh, ["expected_version"] = expected, ["actual_version"] = actual,
        });

    public static DuplicateCandidateError ActorRequired() => new(
        "X-Actor-Id is required to record who made this administrative change", "actor_required", 400);

    public static DuplicateCandidateError InvalidActorType(string actorType) => new(
        "X-Actor-Type must be one of user, service_account, device, system, anonymous, unknown", "invalid_actor_type", 400,
        new Dictionary<string, object?> { ["actor_type"] = actorType });

    public static DuplicateCandidateError Validation(IReadOnlyList<Api.ValidationErrorItem> errors) => new(
        "the request failed validation", "validation_failed", 422)
    { Errors = errors };

    public static DuplicateCandidateError AuditUnavailable() => new(
        "the audit sink failed; the change was not committed without an audit record", "audit_unavailable", 503);

    public static DuplicateCandidateError TenantContextMissing() => new(
        "tenant_context_missing: no tenant channel has resolved a context for this request", "tenant_context_missing", 400);

    /// <summary>Repository-boundary belt-and-braces check (dual-boundary discipline, BIO-DEV-001-QA-04 / BIO-IDN-001-QA-01).</summary>
    public static DuplicateCandidateError IdentityImmutable(string personIdLow, string personIdHigh) => new(
        $"a duplicate candidate's tenant_id, person_id_low and person_id_high may never change once created: {personIdLow}/{personIdHigh}",
        "identity_immutable", 500,
        new Dictionary<string, object?> { ["person_id_low"] = personIdLow, ["person_id_high"] = personIdHigh });

    public static DuplicateCandidateError InvalidInitialState(string personIdLow, string personIdHigh, DuplicateReviewStatus status, int version) => new(
        $"a newly created duplicate candidate must start PENDING_REVIEW at version 1: {personIdLow}/{personIdHigh}", "invalid_initial_state", 500,
        new Dictionary<string, object?>
        {
            ["person_id_low"] = personIdLow, ["person_id_high"] = personIdHigh, ["status"] = status.ToWireString(), ["version"] = version,
        });

    // --- BIO-IDN-005-QA-01: PersonContactPoint (see that type's header) ---

    public static DuplicateCandidateError ContactPointNotFound(string personId) => new(
        $"no contact point is on file for this person: {personId}", "contact_point_not_found", 404,
        new Dictionary<string, object?> { ["person_id"] = personId });

    public static DuplicateCandidateError ContactPointConcurrencyConflict(string personId, int expected, int actual) => new(
        "the contact point was modified since the version this request expected", "concurrency_conflict", 409,
        new Dictionary<string, object?> { ["person_id"] = personId, ["expected_version"] = expected, ["actual_version"] = actual });

    /// <summary>Repository-boundary belt-and-braces check (dual-boundary discipline, BIO-DEV-001-QA-04 / BIO-IDN-001-QA-01).</summary>
    public static DuplicateCandidateError ContactPointIdentityImmutable(string personId) => new(
        $"a contact point's tenant_id and person_id may never change once created: {personId}", "identity_immutable", 500,
        new Dictionary<string, object?> { ["person_id"] = personId });

    public static DuplicateCandidateError InvalidContactPointInitialState(string personId, int version) => new(
        $"a newly created contact point must start at version 1: {personId}", "invalid_initial_state", 500,
        new Dictionary<string, object?> { ["person_id"] = personId, ["version"] = version });
}

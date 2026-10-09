namespace Biolog.Features.AccessPolicy.Domain;

/// <summary>
/// This feature's domain error, shaped identically to every sibling story's so it duck-types
/// cleanly onto <see cref="Api.ProblemResponse"/>'s builder (ADR-0004 §2.1) - mirrors
/// BIO-IDN-006's own <c>RbacError</c>.
/// </summary>
public sealed class AccessPolicyError(string message, string code, int status, IReadOnlyDictionary<string, object?>? detail = null)
    : Exception(message)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
    public IReadOnlyDictionary<string, object?> Detail { get; } = detail ?? new Dictionary<string, object?>();

    public IReadOnlyList<Api.ValidationErrorItem>? Errors { get; init; }

    public static AccessPolicyError ActorRequired() => new(
        "X-Actor-Id is required to record who made this administrative change", "actor_required", 400);

    public static AccessPolicyError InvalidActorType(string actorType) => new(
        "X-Actor-Type must be one of user, service_account, device, system, anonymous, unknown", "invalid_actor_type", 400,
        new Dictionary<string, object?> { ["actor_type"] = actorType });

    /// <summary>ADR-0006 Decision 3, applied from the start (see <see cref="Api.ActorProvenanceGate"/>): identical for a missing proof and a wrong one, so a caller cannot distinguish the two.</summary>
    public static AccessPolicyError ActorProvenanceRequired() => new(
        "this action requires a valid X-Actor-Provenance proof for the asserted X-Actor-Id (ADR-0006 Decision 3) - a server-computed HMAC bound to that specific actor id, distinct from the caller-asserted X-Actor-Id itself",
        "actor_provenance_required", 401);

    public static AccessPolicyError Validation(IReadOnlyList<Api.ValidationErrorItem> errors) => new(
        "the request failed validation", "validation_failed", 422)
    { Errors = errors };

    public static AccessPolicyError AuditUnavailable() => new(
        "the audit sink failed; the change was not committed without an audit record", "audit_unavailable", 503);

    public static AccessPolicyError TenantContextMissing() => new(
        "tenant_context_missing: no tenant channel has resolved a context for this request", "tenant_context_missing", 400);

    public static AccessPolicyError PermissionRequired(string permissionKey) => new(
        $"this action requires the calling actor to hold the '{permissionKey}' permission", "permission_required", 403,
        new Dictionary<string, object?> { ["permission_key"] = permissionKey });

    public static AccessPolicyError PolicyNotFound(string policyKey) => new(
        $"access policy not found: {policyKey}", "policy_not_found", 404,
        new Dictionary<string, object?> { ["policy_key"] = policyKey });

    public static AccessPolicyError PolicyAlreadyExists(string policyKey) => new(
        $"an access policy with this key already exists: {policyKey}", "policy_already_exists", 409,
        new Dictionary<string, object?> { ["policy_key"] = policyKey });

    public static AccessPolicyError ConcurrencyConflict(string policyKey, int expected, int actual) => new(
        "the access policy was modified since the version this request expected", "concurrency_conflict", 409,
        new Dictionary<string, object?> { ["policy_key"] = policyKey, ["expected_version"] = expected, ["actual_version"] = actual });

    public static AccessPolicyError PolicyVersionNotDraft(string policyKey, AccessPolicyStatus status) => new(
        $"the latest version of '{policyKey}' is not a draft: {status}", "policy_version_not_draft", 409,
        new Dictionary<string, object?> { ["policy_key"] = policyKey, ["status"] = status.ToWireString() });

    /// <summary>BIO-ACC-001-AC02's maker-checker on publish, the same "approver must differ from author" rule BIO-DOC-005's own <c>ParserRecord.Publish</c> already established for this exact shape.</summary>
    public static AccessPolicyError SelfApprovalForbidden() => new(
        "the actor who authored this draft may not also publish it (maker-checker separation of duties)", "self_approval_forbidden", 403);

    public static AccessPolicyError PolicyIdentityImmutable(string policyKey) => new(
        $"an access policy's tenant_id and policy_key may never change once created: {policyKey}", "identity_immutable", 500,
        new Dictionary<string, object?> { ["policy_key"] = policyKey });

    /// <summary>BIO-IDN-002-QA-01's lesson, applied here: a rule naming a specific subject must reference a person who genuinely exists in the resolved tenant.</summary>
    public static AccessPolicyError SubjectPersonNotFound(string personId) => new(
        $"person not found in the resolved tenant: {personId}", "subject_person_not_found", 404,
        new Dictionary<string, object?> { ["person_id"] = personId });

    public static AccessPolicyError PersonNotFound(string personId) => new(
        $"person not found in the resolved tenant: {personId}", "person_not_found", 404,
        new Dictionary<string, object?> { ["person_id"] = personId });
}

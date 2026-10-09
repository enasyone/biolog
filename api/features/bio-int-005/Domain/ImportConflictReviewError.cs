namespace Biolog.Features.ImportConflictReview.Domain;

/// <summary>This feature's domain error, shaped identically to every sibling story's so it duck-types cleanly onto <see cref="Api.ProblemResponse"/>'s builder (ADR-0004 §2.1).</summary>
public sealed class ImportConflictReviewError(string message, string code, int status, IReadOnlyDictionary<string, object?>? detail = null)
    : Exception(message)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
    public IReadOnlyDictionary<string, object?> Detail { get; } = detail ?? new Dictionary<string, object?>();

    public IReadOnlyList<Api.ValidationErrorItem>? Errors { get; init; }

    public static ImportConflictReviewError ActorRequired() => new(
        "X-Actor-Id is required to record who requested this change", "actor_required", 400);

    public static ImportConflictReviewError InvalidActorType(string actorType) => new(
        "X-Actor-Type must be one of user, service_account, device, system, anonymous, unknown", "invalid_actor_type", 400,
        new Dictionary<string, object?> { ["actor_type"] = actorType });

    /// <summary>ADR-0006 Decision 3: required only on the decide endpoint - this feature's own authorization decision. Scan/list stays audit-attribution-only (ADR-0006 Decision 2), mirroring BIO-INT-002's identical preview/apply split.</summary>
    public static ImportConflictReviewError ActorProvenanceRequired() => new(
        "this action requires a valid X-Actor-Provenance proof for the asserted X-Actor-Id (ADR-0006 Decision 3) - a server-computed HMAC bound to that specific actor id, distinct from the caller-asserted X-Actor-Id itself",
        "actor_provenance_required", 401);

    public static ImportConflictReviewError Validation(IReadOnlyList<Api.ValidationErrorItem> errors) => new(
        "the request failed validation", "validation_failed", 422)
    { Errors = errors };

    public static ImportConflictReviewError AuditUnavailable() => new(
        "the audit sink failed; the change was not committed without an audit record", "audit_unavailable", 503);

    public static ImportConflictReviewError TenantContextMissing() => new(
        "tenant_context_missing: no tenant channel has resolved a context for this request", "tenant_context_missing", 400);

    public static ImportConflictReviewError PermissionRequired(string permissionKey) => new(
        $"this action requires the calling actor to hold the '{permissionKey}' permission", "permission_required", 403,
        new Dictionary<string, object?> { ["permission_key"] = permissionKey });

    public static ImportConflictReviewError MappingIdRequired() => new(
        "mappingId must not be blank", "mapping_id_required", 400);

    public static ImportConflictReviewError NotFound(string mappingId, string externalKey) => new(
        $"import conflict review not found for mapping '{mappingId}', external key '{externalKey}'", "import_conflict_review_not_found", 404,
        new Dictionary<string, object?> { ["mapping_id"] = mappingId, ["external_key"] = externalKey });

    public static ImportConflictReviewError ConcurrencyConflict(string mappingId, string externalKey, int expected, int actual) => new(
        "the import conflict review was modified since the version this request expected", "concurrency_conflict", 409,
        new Dictionary<string, object?>
        {
            ["mapping_id"] = mappingId, ["external_key"] = externalKey, ["expected_version"] = expected, ["actual_version"] = actual,
        });

    /// <summary>Repository-boundary belt-and-braces check (dual-boundary discipline, BIO-DEV-001-QA-04 / BIO-IDN-001-QA-01).</summary>
    public static ImportConflictReviewError IdentityImmutable(string mappingId, string externalKey) => new(
        $"an import conflict review's tenant_id, mapping_id and external_key may never change once created: {mappingId}/{externalKey}",
        "identity_immutable", 500,
        new Dictionary<string, object?> { ["mapping_id"] = mappingId, ["external_key"] = externalKey });

    public static ImportConflictReviewError InvalidInitialState(string mappingId, string externalKey, ImportConflictReviewStatus status, int version) => new(
        $"a newly created import conflict review must start PENDING_REVIEW at version 1: {mappingId}/{externalKey}", "invalid_initial_state", 500,
        new Dictionary<string, object?>
        {
            ["mapping_id"] = mappingId, ["external_key"] = externalKey, ["status"] = status.ToWireString(), ["version"] = version,
        });
}

namespace Biolog.Features.Integrations.Domain;

/// <summary>This feature's domain error, shaped identically to every sibling story's so it duck-types cleanly onto <see cref="Api.ProblemResponse"/>'s builder (ADR-0004 S2.1).</summary>
public sealed class ImportError(string message, string code, int status, IReadOnlyDictionary<string, object?>? detail = null)
    : Exception(message)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
    public IReadOnlyDictionary<string, object?> Detail { get; } = detail ?? new Dictionary<string, object?>();

    public IReadOnlyList<Api.ValidationErrorItem>? Errors { get; init; }

    public static ImportError ActorRequired() => new(
        "X-Actor-Id is required to record who requested this change", "actor_required", 400);

    public static ImportError InvalidActorType(string actorType) => new(
        "X-Actor-Type must be one of user, service_account, device, system, anonymous, unknown", "invalid_actor_type", 400,
        new Dictionary<string, object?> { ["actor_type"] = actorType });

    /// <summary>ADR-0006 Decision 3: required only on mapping-write and apply - the authorization decisions this feature makes. Preview/read stays audit-attribution-only (ADR-0006 Decision 2).</summary>
    public static ImportError ActorProvenanceRequired() => new(
        "this action requires a valid X-Actor-Provenance proof for the asserted X-Actor-Id (ADR-0006 Decision 3) - a server-computed HMAC bound to that specific actor id, distinct from the caller-asserted X-Actor-Id itself",
        "actor_provenance_required", 401);

    public static ImportError Validation(IReadOnlyList<Api.ValidationErrorItem> errors) => new(
        "the request failed validation", "validation_failed", 422)
    { Errors = errors };

    public static ImportError AuditUnavailable() => new(
        "the audit sink failed; the change was not committed without an audit record", "audit_unavailable", 503);

    public static ImportError TenantContextMissing() => new(
        "tenant_context_missing: no tenant channel has resolved a context for this request", "tenant_context_missing", 400);

    public static ImportError PermissionRequired(string permissionKey) => new(
        $"this action requires the calling actor to hold the '{permissionKey}' permission", "permission_required", 403,
        new Dictionary<string, object?> { ["permission_key"] = permissionKey });

    public static ImportError MappingNotFound(string mappingId) => new(
        $"import source mapping not found: {mappingId}", "import_mapping_not_found", 404,
        new Dictionary<string, object?> { ["mapping_id"] = mappingId });

    public static ImportError ConcurrencyConflict(string mappingId, int expected, int actual) => new(
        $"import source mapping '{mappingId}' was modified since the version this request expected", "concurrency_conflict", 409,
        new Dictionary<string, object?> { ["mapping_id"] = mappingId, ["expected_version"] = expected, ["actual_version"] = actual });

    /// <summary>AC02: BIOLOG is configured as the system of record for this integration - an inbound apply is rejected outright, never silently accepted and discarded.</summary>
    public static ImportError BiologIsMasterForThisIntegration(string mappingId) => new(
        $"import source mapping '{mappingId}' is configured with BIOLOG as the system of record (sync_direction=biolog_is_master); this integration does not accept inbound data", "biolog_is_master", 409,
        new Dictionary<string, object?> { ["mapping_id"] = mappingId });

    /// <summary>BIO-INT-002-F02's own remediation (Zainab round-1 FAIL, ZAINAB-INT-002-F02): an administrator disabled this mapping (<c>Active = false</c>) - the kill switch must actually stop both preview and apply, not just hide the mapping from some other surface.</summary>
    public static ImportError MappingDisabled(string mappingId) => new(
        $"import source mapping '{mappingId}' is disabled (active=false); preview and apply are both rejected until it is re-enabled", "import_mapping_disabled", 409,
        new Dictionary<string, object?> { ["mapping_id"] = mappingId });

    public static ImportError BatchNotFound(string batchId) => new(
        $"import batch not found: {batchId}", "import_batch_not_found", 404,
        new Dictionary<string, object?> { ["batch_id"] = batchId });

    /// <summary>ADR-0004's idempotency contract: a replay of the same Idempotency-Key with a DIFFERENT request body is a client bug, never silently accepted as if it were the original.</summary>
    public static ImportError IdempotencyKeyReused(string idempotencyKey) => new(
        $"Idempotency-Key '{idempotencyKey}' was already used for a request with different content", "idempotency_key_reused", 409,
        new Dictionary<string, object?> { ["idempotency_key"] = idempotencyKey });

    public static ImportError IdempotencyKeyRequired() => new(
        "Idempotency-Key header is required for this operation (ADR-0004 Decision 4)", "idempotency_key_required", 400);

    /// <summary>BIO-INT-002-F01's own remediation (Seyi round-1 FAIL): a concurrent caller for the same Idempotency-Key is still in flight and did not complete within the bounded wait - ADR-0004/BIO-ARC-004's documented "claim-loser waits bounded then idempotency_wait_timeout (503)" behavior, never silently executed a second time.</summary>
    public static ImportError IdempotencyWaitTimeout(string idempotencyKey) => new(
        $"another request with idempotency key '{idempotencyKey}' is still in flight and did not complete within the wait window", "idempotency_wait_timeout", 503,
        new Dictionary<string, object?> { ["idempotency_key"] = idempotencyKey });

    /// <summary>
    /// BIO-INT-002-F04's own remediation (Zainab round-3 FAIL, ZAINAB-INT-002-F04): two rows in one
    /// request sharing an external key would otherwise let row two silently replay row one's
    /// <see cref="IImportRowProvenanceLedger"/> entry without ever evaluating its own content,
    /// defeating conflict detection for row two. Rejected up front, before any row is processed,
    /// rather than built around with a per-row content fingerprint - see
    /// <see cref="ImportBatchService"/>'s own class header for why this is sufficient.
    /// </summary>
    public static ImportError DuplicateExternalKeysInRequest(IReadOnlyList<string> duplicateExternalKeys) => new(
        "the request contains more than one row with the same external key; each row must have a unique external key", "duplicate_external_keys", 422,
        new Dictionary<string, object?> { ["duplicate_external_keys"] = duplicateExternalKeys });
}

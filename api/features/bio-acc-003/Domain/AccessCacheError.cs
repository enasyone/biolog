namespace Biolog.Features.AccessCache.Domain;

/// <summary>This feature's domain error, shaped identically to every sibling story's so it duck-types cleanly onto <see cref="Api.ProblemResponse"/>'s builder (ADR-0004 §2.1).</summary>
public sealed class AccessCacheError(string message, string code, int status, IReadOnlyDictionary<string, object?>? detail = null)
    : Exception(message)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
    public IReadOnlyDictionary<string, object?> Detail { get; } = detail ?? new Dictionary<string, object?>();

    public IReadOnlyList<Api.ValidationErrorItem>? Errors { get; init; }

    public static AccessCacheError Validation(IReadOnlyList<Api.ValidationErrorItem> errors) => new(
        "the request failed validation", "validation_failed", 422)
    { Errors = errors };

    /// <summary>Identical for a missing/malformed credential header pair, an unregistered device, an inactive device, and a wrong secret - mirrors BIO-MOB-003/BIO-PHO-002's own disclosed scope: a caller cannot use this endpoint to enumerate which device ids are registered or active.</summary>
    public static AccessCacheError DeviceAuthenticationFailed() => new(
        "device authentication failed", "device_authentication_failed", 401);

    public static AccessCacheError IdempotencyKeyRequired() => new(
        "Idempotency-Key is required on this write endpoint (ADR-0001 §3.4)", "idempotency_key_required", 428);

    public static AccessCacheError IdempotencyKeyReuse() => new(
        "this Idempotency-Key was already used with different request content", "idempotency_key_reuse", 409);

    /// <summary>Idempotency-claim-race remediation (Tunde/Seyi/Zainab's finding, Kemi-confirmed High severity): a concurrent caller for the same Idempotency-Key is still in flight and did not complete within the bounded wait - the claim-loser waits bounded then fails closed, never silently executed a second time.</summary>
    public static AccessCacheError IdempotencyWaitTimeout(string idempotencyKey) => new(
        $"another request with idempotency key '{idempotencyKey}' is still in flight and did not complete within the wait window", "idempotency_wait_timeout", 503,
        new Dictionary<string, object?> { ["idempotency_key"] = idempotencyKey });

    /// <summary>Mirrors BIO-MOB-003's TenantMismatch: a presentation event's body-claimed tenant_id must match the tenant independently resolved from the authenticated device (ADR-0002 Decision 1.1 - a body field is not a channel).</summary>
    public static AccessCacheError TenantMismatch(string claimedTenantId, string resolvedTenantId) => new(
        "the request body's tenant_id does not match the tenant resolved from the authenticated device",
        "tenant_mismatch", 403,
        new Dictionary<string, object?> { ["claimed_tenant_id"] = claimedTenantId, ["resolved_tenant_id"] = resolvedTenantId });

    public static AccessCacheError DeviceMismatch(string claimedDeviceId, string resolvedDeviceId) => new(
        "the request body's device_id does not match the authenticated device",
        "device_mismatch", 403,
        new Dictionary<string, object?> { ["claimed_device_id"] = claimedDeviceId, ["resolved_device_id"] = resolvedDeviceId });

    public static AccessCacheError AuditUnavailable() => new(
        "the audit sink failed; the change was not committed without an audit record", "audit_unavailable", 503);

    public static AccessCacheError ActorRequired() => new(
        "X-Actor-Id is required (ADR-0006 Decision 3, pending BIO-INT-001)", "actor_required", 401);

    public static AccessCacheError InvalidActorType(string value) => new(
        $"'{value}' is not a recognised actor type", "invalid_actor_type", 422);

    public static AccessCacheError ActorProvenanceRequired() => new(
        "X-Actor-Provenance is required and must verify against the resolved actor id (ADR-0006 Decision 3)",
        "actor_provenance_required", 401);

    public static AccessCacheError TenantContextMissing() => new(
        "no tenant could be resolved for this request (ADR-0002 Decision 2)", "tenant_context_missing", 403);

    public static AccessCacheError Forbidden(string permissionKey) => new(
        "the calling actor does not hold the required permission", "forbidden", 403,
        new Dictionary<string, object?> { ["permission_key"] = permissionKey });
}

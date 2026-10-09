namespace Biolog.Features.OfflineSync.Domain;

/// <summary>This feature's domain error, shaped identically to every sibling story's so it duck-types cleanly onto <see cref="Api.ProblemResponse"/>'s builder (ADR-0004 §2.1).</summary>
public sealed class OfflineSyncError(string message, string code, int status, IReadOnlyDictionary<string, object?>? detail = null)
    : Exception(message)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
    public IReadOnlyDictionary<string, object?> Detail { get; } = detail ?? new Dictionary<string, object?>();

    public IReadOnlyList<Api.ValidationErrorItem>? Errors { get; init; }

    public static OfflineSyncError Validation(IReadOnlyList<Api.ValidationErrorItem> errors) => new(
        "the request failed validation", "validation_failed", 422)
    { Errors = errors };

    /// <summary>BIO-MOB-003: identical for a missing/malformed credential header pair, an unregistered device, an inactive device, and a wrong secret - a caller cannot use this endpoint to enumerate which device ids are registered or active. Mirrors BIO-PHO-002-AC02's own disclosed scope exactly.</summary>
    public static OfflineSyncError DeviceAuthenticationFailed() => new(
        "device authentication failed", "device_authentication_failed", 401);

    public static OfflineSyncError IdempotencyKeyRequired() => new(
        "Idempotency-Key is required on this write endpoint (ADR-0001 §3.4)", "idempotency_key_required", 428);

    public static OfflineSyncError IdempotencyKeyReuse() => new(
        "this Idempotency-Key was already used with different request content", "idempotency_key_reuse", 409);

    /// <summary>Idempotency-claim-race remediation (Tunde/Seyi/Zainab's finding, Kemi-confirmed High severity): a concurrent caller for the same Idempotency-Key is still in flight and did not complete within the bounded wait - the claim-loser waits bounded then fails closed, never silently executed a second time.</summary>
    public static OfflineSyncError IdempotencyWaitTimeout(string idempotencyKey) => new(
        $"another request with idempotency key '{idempotencyKey}' is still in flight and did not complete within the wait window", "idempotency_wait_timeout", 503,
        new Dictionary<string, object?> { ["idempotency_key"] = idempotencyKey });

    /// <summary>Kemi's architectural ruling on BIO-MOB-003's acceptance FAIL: `offline-mutation.schema.json`'s `tenant_id` is a request BODY field, never trusted as a routing channel (ADR-0002 Decision 1.1 - a body field is not a channel). This is what firing when the payload's own claimed tenant_id does not match the tenant independently resolved from the authenticated device's own registered record - the cross-tenant-substitution defence, not a validation nicety.</summary>
    public static OfflineSyncError TenantMismatch(string claimedTenantId, string resolvedTenantId) => new(
        "the request body's tenant_id does not match the tenant resolved from the authenticated device",
        "tenant_mismatch", 403,
        new Dictionary<string, object?> { ["claimed_tenant_id"] = claimedTenantId, ["resolved_tenant_id"] = resolvedTenantId });

    /// <summary>BIO-MOB-003-QA-02 (Chioma, acceptance round 2): the same claim-vs-resolution discipline as <see cref="TenantMismatch"/>, applied to the payload's `device_id`. Nothing previously checked that the body's claimed `device_id` was the SAME device that actually authenticated the request - an authenticated device could submit another device's canonical id as the mutation's claimed origin.</summary>
    public static OfflineSyncError DeviceMismatch(string claimedDeviceId, string resolvedDeviceId) => new(
        "the request body's device_id does not match the authenticated device",
        "device_mismatch", 403,
        new Dictionary<string, object?> { ["claimed_device_id"] = claimedDeviceId, ["resolved_device_id"] = resolvedDeviceId });

    /// <summary>ADR-0001 §3.2/§3.5: `base_version` no longer matches this ledger's current version for the aggregate. `currentVersion` is echoed back per the ADR's own table ("response carries the current version") - never used by this server to auto-resolve, only to report.</summary>
    public static OfflineSyncError ConcurrencyConflict(string aggregateId, int expected, int actual) => new(
        "base_version does not match the current version for this aggregate", "concurrency_conflict", 409,
        new Dictionary<string, object?> { ["aggregate_id"] = aggregateId, ["expected_version"] = expected, ["current_version"] = actual });

    public static OfflineSyncError AuditUnavailable() => new(
        "the audit sink failed; the change was not committed without an audit record", "audit_unavailable", 503);
}

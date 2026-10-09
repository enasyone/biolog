namespace Biolog.Features.Photography.Domain;

/// <summary>
/// This feature's domain error, shaped identically to every sibling story's so it duck-types
/// cleanly onto <see cref="Api.ProblemResponse"/>'s builder (ADR-0004 §2.1).
/// </summary>
public sealed class PhotoError(string message, string code, int status, IReadOnlyDictionary<string, object?>? detail = null)
    : Exception(message)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
    public IReadOnlyDictionary<string, object?> Detail { get; } = detail ?? new Dictionary<string, object?>();

    public IReadOnlyList<Api.ValidationErrorItem>? Errors { get; init; }

    /// <summary>Applying BIO-IDN-002-QA-01's lesson from the start - see IPersonExistenceGateway's header. Deliberately the same code/status/shape for missing and foreign-tenant (ADR-0001 §1.5, no enumeration oracle).</summary>
    public static PhotoError PersonNotFound(string personId) => new(
        $"person not found in the resolved tenant: {personId}", "person_not_found", 404,
        new Dictionary<string, object?> { ["person_id"] = personId });

    public static PhotoError ActorRequired() => new(
        "X-Actor-Id is required to record who made this change", "actor_required", 400);

    public static PhotoError InvalidActorType(string actorType) => new(
        "X-Actor-Type must be one of user, service_account, device, system, anonymous, unknown", "invalid_actor_type", 400,
        new Dictionary<string, object?> { ["actor_type"] = actorType });

    public static PhotoError Validation(IReadOnlyList<Api.ValidationErrorItem> errors) => new(
        "the request failed validation", "validation_failed", 422)
    { Errors = errors };

    public static PhotoError AuditUnavailable() => new(
        "the audit sink failed; the change was not committed without an audit record", "audit_unavailable", 503);

    public static PhotoError TenantContextMissing() => new(
        "tenant_context_missing: no tenant channel has resolved a context for this request", "tenant_context_missing", 400);

    public static PhotoError UnsupportedFormat() => new(
        "only PNG and JPEG photo captures are supported", "unsupported_format", 422);

    // --- PhotoMaster ---

    public static PhotoError MasterNotFound(string personId, int? version = null) => new(
        version is null
            ? $"no photo master exists for this person: {personId}"
            : $"photo master version not found: {personId}/v{version}",
        "photo_master_not_found", 404,
        version is null
            ? new Dictionary<string, object?> { ["person_id"] = personId }
            : new Dictionary<string, object?> { ["person_id"] = personId, ["version"] = version });

    /// <summary>Repository-boundary belt-and-braces check (ADR-0001 §3.3): a version already
    /// recorded for this person may never be appended again - unreachable via any legitimate
    /// call, since <see cref="PhotoMasterService"/> always computes the next version itself
    /// under <see cref="PhotoMasterLockRegistry"/>.</summary>
    public static PhotoError MasterVersionAlreadyExists(string personId, int version) => new(
        $"a photo master already exists at this version: {personId}/v{version}", "photo_master_version_conflict", 500,
        new Dictionary<string, object?> { ["person_id"] = personId, ["version"] = version });

    // --- PhotoDerivative ---

    public static PhotoError DerivativeNotFound(string personId, int masterVersion, Domain.DerivativeKind kind) => new(
        $"no {kind.ToWireString()} derivative exists for this master: {personId}/v{masterVersion}", "photo_derivative_not_found", 404,
        new Dictionary<string, object?> { ["person_id"] = personId, ["master_version"] = masterVersion, ["kind"] = kind.ToWireString() });

    // --- PhotoAssignment ---

    public static PhotoError AssignmentConcurrencyConflict(string personId, int expected, int actual) => new(
        "the photo assignment was modified since the version this request expected", "concurrency_conflict", 409,
        new Dictionary<string, object?> { ["person_id"] = personId, ["expected_version"] = expected, ["actual_version"] = actual });

    public static PhotoError PreconditionRequired(string personId) => new(
        "expected_version is required on a mutating write once a photo assignment exists (ADR-0001 §3.2)", "precondition_required", 428,
        new Dictionary<string, object?> { ["person_id"] = personId });

    public static PhotoError AssignmentAlreadyExists(string personId) => new(
        $"a photo assignment already exists for this person; supply its expected_version to update it: {personId}",
        "concurrency_conflict", 409,
        new Dictionary<string, object?> { ["person_id"] = personId });
}

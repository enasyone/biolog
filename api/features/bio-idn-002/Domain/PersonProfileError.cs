namespace Biolog.Features.PersonProfileRegistry.Domain;

/// <summary>
/// This feature's domain error, shaped identically to every sibling story's
/// (<c>PersonError</c>, <c>TenantDomainError</c>, <c>TenantRegistryError</c>) so it duck-types
/// cleanly onto <see cref="Api.ProblemResponse"/>'s builder (ADR-0004 §2.1).
/// </summary>
public sealed class PersonProfileError(string message, string code, int status, IReadOnlyDictionary<string, object?>? detail = null)
    : Exception(message)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
    public IReadOnlyDictionary<string, object?> Detail { get; } = detail ?? new Dictionary<string, object?>();

    public IReadOnlyList<Api.ValidationErrorItem>? Errors { get; init; }

    /// <summary>
    /// BIO-IDN-002-QA-01 (Seyi round 1): the resolved-tenant PERSON referenced by an
    /// assignment does not exist - either genuinely missing, or existing only in a different
    /// tenant. Deliberately the SAME code/status/shape for both causes (ADR-0001 §1.5: a
    /// canonical identifier carries no authority; distinguishing "missing" from "exists in
    /// another tenant" here would be a cross-tenant enumeration oracle).
    /// </summary>
    public static PersonProfileError PersonNotFound(string personId) => new(
        $"person not found in the resolved tenant: {personId}", "person_not_found", 404,
        new Dictionary<string, object?> { ["person_id"] = personId });

    public static PersonProfileError NotFound(string personId, ProfileType type) => new(
        $"profile not found: {personId}/{type.ToWireString()}", "person_profile_not_found", 404,
        new Dictionary<string, object?> { ["person_id"] = personId, ["type"] = type.ToWireString() });

    public static PersonProfileError DuplicateProfileType(string personId, ProfileType type) => new(
        $"person already has a {type.ToWireString()} profile: {personId}", "duplicate_profile_type", 409,
        new Dictionary<string, object?> { ["person_id"] = personId, ["type"] = type.ToWireString() });

    public static PersonProfileError ConcurrencyConflict(string personId, ProfileType type, int expected, int actual) => new(
        "the profile was modified since the version this request expected", "concurrency_conflict", 409,
        new Dictionary<string, object?>
        {
            ["person_id"] = personId, ["type"] = type.ToWireString(), ["expected_version"] = expected, ["actual_version"] = actual,
        });

    /// <summary>BIO-IDN-002's only governed transition: Active -&gt; Revoked. Revoked is terminal.</summary>
    public static PersonProfileError AlreadyRevoked(string personId, ProfileType type) => new(
        $"profile is already revoked: {personId}/{type.ToWireString()}", "already_revoked", 409,
        new Dictionary<string, object?> { ["person_id"] = personId, ["type"] = type.ToWireString() });

    public static PersonProfileError ActorRequired() => new(
        "X-Actor-Id is required to record who made this administrative change", "actor_required", 400);

    public static PersonProfileError InvalidActorType(string actorType) => new(
        "X-Actor-Type must be one of user, service_account, device, system, anonymous, unknown", "invalid_actor_type", 400,
        new Dictionary<string, object?> { ["actor_type"] = actorType });

    public static PersonProfileError Validation(IReadOnlyList<Api.ValidationErrorItem> errors) => new(
        "the request failed validation", "validation_failed", 422)
    { Errors = errors };

    public static PersonProfileError AuditUnavailable() => new(
        "the audit sink failed; the change was not committed without an audit record", "audit_unavailable", 503);

    public static PersonProfileError TenantContextMissing() => new(
        "tenant_context_missing: no tenant channel has resolved a context for this request", "tenant_context_missing", 400);

    /// <summary>BIO-IDN-002-QA-01-preempted (applying BIO-IDN-001's lesson from the start): the persistence-boundary belt-and-braces check in <see cref="InMemoryPersonProfileRepository.ReplaceAsync"/>.</summary>
    public static PersonProfileError IdentityImmutable(string personId, ProfileType type) => new(
        $"a profile's person_id, tenant_id and type may never change once created: {personId}/{type.ToWireString()}",
        "identity_immutable", 500,
        new Dictionary<string, object?> { ["person_id"] = personId, ["type"] = type.ToWireString() });

    public static PersonProfileError InvalidInitialState(string personId, ProfileType type, ProfileStatus status, int version) => new(
        $"a newly created profile must start ACTIVE at version 1: {personId}/{type.ToWireString()}", "invalid_initial_state", 500,
        new Dictionary<string, object?>
        {
            ["person_id"] = personId, ["type"] = type.ToWireString(), ["status"] = status.ToWireString(), ["version"] = version,
        });
}

namespace Biolog.Features.PersonRegistry.Domain;

/// <summary>
/// This feature's domain error, shaped identically to <c>TenantContextError</c> /
/// <c>ConcurrencyError</c> / <c>AuditError</c> in the contracts (ADR-0004 §2.1: "(message,
/// code, status, detail) ... without any of them extending a common base"), matching
/// <c>TenantDomainError</c> (BIO-TEN-002) and <c>TenantRegistryError</c> (BIO-TEN-001)
/// exactly.
/// </summary>
public sealed class PersonError(string message, string code, int status, IReadOnlyDictionary<string, object?>? detail = null)
    : Exception(message)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
    public IReadOnlyDictionary<string, object?> Detail { get; } = detail ?? new Dictionary<string, object?>();

    public IReadOnlyList<Api.ValidationErrorItem>? Errors { get; init; }

    public static PersonError NotFound(string personId) => new(
        $"person not found: {personId}", "person_not_found", 404,
        new Dictionary<string, object?> { ["person_id"] = personId });

    public static PersonError ConcurrencyConflict(string personId, int expected, int actual) => new(
        "the person record was modified since the version this request expected", "concurrency_conflict", 409,
        new Dictionary<string, object?> { ["person_id"] = personId, ["expected_version"] = expected, ["actual_version"] = actual });

    /// <summary>BIO-IDN-001-AC03: attempted transition is not in this story's baseline table (see <see cref="Person"/>'s header).</summary>
    public static PersonError InvalidStatusTransition(PersonStatus from, PersonStatus to) => new(
        $"cannot transition a person from {from.ToWireString()} to {to.ToWireString()}", "invalid_status_transition", 409,
        new Dictionary<string, object?> { ["from_status"] = from.ToWireString(), ["to_status"] = to.ToWireString() });

    /// <summary>
    /// BIO-IDN-001-QA-01 (Seyi round 1): the persistence-boundary belt-and-braces check in
    /// <see cref="InMemoryPersonRepository.ReplaceAsync"/>. Should never happen in production
    /// - <see cref="PersonService"/> never constructs an <c>updated</c> record with a
    /// different <see cref="Person.Id"/> or <see cref="Person.TenantId"/> than the
    /// <c>expected</c> record it read - but the repository boundary enforces it independently
    /// rather than trusting the caller, the same dual-boundary discipline
    /// <c>CredentialReferenceValidator</c> (BIO-DEV-001-QA-04) established.
    /// </summary>
    public static PersonError IdentityOrTenantImmutable(string personId) => new(
        $"a person's id and tenant_id may never change once created: {personId}", "identity_or_tenant_immutable", 500,
        new Dictionary<string, object?> { ["person_id"] = personId });

    /// <summary>BIO-IDN-001-QA-01: <see cref="InMemoryPersonRepository.CreateAsync"/>'s belt-and-braces check - see <see cref="IdentityOrTenantImmutable"/>'s header for the dual-boundary rationale.</summary>
    public static PersonError InvalidInitialState(string personId, PersonStatus status, int version) => new(
        $"a newly created person must start PRE_ENROLLED at version 1: {personId}", "invalid_initial_state", 500,
        new Dictionary<string, object?> { ["person_id"] = personId, ["status"] = status.ToWireString(), ["version"] = version });

    public static PersonError ActorRequired() => new(
        "X-Actor-Id is required to record who made this administrative change", "actor_required", 400);

    public static PersonError InvalidActorType(string actorType) => new(
        "X-Actor-Type must be one of user, service_account, device, system, anonymous, unknown", "invalid_actor_type", 400,
        new Dictionary<string, object?> { ["actor_type"] = actorType });

    public static PersonError Validation(IReadOnlyList<Api.ValidationErrorItem> errors) => new(
        "the request failed validation", "validation_failed", 422)
    { Errors = errors };

    public static PersonError AuditUnavailable() => new(
        "the audit sink failed; the change was not committed without an audit record", "audit_unavailable", 503);

    /// <summary>
    /// ADR-0002 Decision 2 / BIO-IDN-001-AC02: no channel resolved a tenant for this request.
    /// Status 400 matches the established convention (<c>tests/dotnet/Biolog.IntegrationTests/DeviceRegistryTests.cs</c>:
    /// "Routed (400 tenant_context_missing), not a 404 from an unmatched route").
    /// </summary>
    public static PersonError TenantContextMissing() => new(
        "tenant_context_missing: no tenant channel has resolved a context for this request", "tenant_context_missing", 400);
}

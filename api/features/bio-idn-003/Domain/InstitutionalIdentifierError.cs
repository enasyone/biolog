namespace Biolog.Features.InstitutionalIdentifiers.Domain;

/// <summary>
/// This feature's domain error, shaped identically to every sibling story's
/// (<c>PersonError</c>, <c>PersonProfileError</c>, <c>TenantDomainError</c>) so it duck-types
/// cleanly onto <see cref="Api.ProblemResponse"/>'s builder (ADR-0004 §2.1).
/// </summary>
public sealed class InstitutionalIdentifierError(string message, string code, int status, IReadOnlyDictionary<string, object?>? detail = null)
    : Exception(message)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
    public IReadOnlyDictionary<string, object?> Detail { get; } = detail ?? new Dictionary<string, object?>();

    public IReadOnlyList<Api.ValidationErrorItem>? Errors { get; init; }

    /// <summary>
    /// Applying BIO-IDN-002-QA-01's lesson from the start: the resolved-tenant PERSON an
    /// identifier attaches to does not exist - either genuinely missing, or existing only in a
    /// different tenant. Deliberately the SAME code/status/shape for both causes (ADR-0001
    /// §1.5: a canonical identifier carries no authority; distinguishing "missing" from "exists
    /// in another tenant" here would be a cross-tenant enumeration oracle).
    /// </summary>
    public static InstitutionalIdentifierError PersonNotFound(string personId) => new(
        $"person not found in the resolved tenant: {personId}", "person_not_found", 404,
        new Dictionary<string, object?> { ["person_id"] = personId });

    public static InstitutionalIdentifierError IdentifierNotFound(string personId, IdentifierType type, string? customLabel, int sequenceNumber) => new(
        $"identifier not found: {personId}/{type.ToWireString()}/{sequenceNumber}", "institutional_identifier_not_found", 404,
        new Dictionary<string, object?>
        {
            ["person_id"] = personId, ["type"] = type.ToWireString(), ["custom_label"] = customLabel, ["sequence_number"] = sequenceNumber,
        });

    /// <summary>AC03's real structural enforcement point for "at most one current identifier per type": a second ACTIVE record for the same (person, type, custom_label) may never coexist with the first - the caller must Retire it first.</summary>
    public static InstitutionalIdentifierError IdentifierAlreadyActive(string personId, IdentifierType type, string? customLabel) => new(
        $"person already has an active {type.ToWireString()} identifier: {personId}", "identifier_already_active", 409,
        new Dictionary<string, object?> { ["person_id"] = personId, ["type"] = type.ToWireString(), ["custom_label"] = customLabel });

    /// <summary>AC02's real enforcement point when the resolved <see cref="IdentifierUniquenessRule"/> is Enforced. The colliding VALUE is deliberately never echoed back in Detail - see InstitutionalIdentifierService.AssignAsync.</summary>
    public static InstitutionalIdentifierError DuplicateIdentifierValue(string tenantId, IdentifierType type, string? customLabel) => new(
        "this value is already assigned to another person for this identifier type in this tenant", "duplicate_identifier_value", 409,
        new Dictionary<string, object?> { ["type"] = type.ToWireString(), ["custom_label"] = customLabel });

    /// <summary>BIO-IDN-003-QA-01 (Seyi round 1, closure item 2): a tenant may not turn ON enforcement for a (type, custom_label) scope that ALREADY contains two or more records sharing a value - that would record a rule the existing data already violates.</summary>
    public static InstitutionalIdentifierError UniquenessAlreadyViolated(IdentifierType type, string? customLabel) => new(
        "one or more values are already duplicated in this scope; resolve the duplicates before enabling enforcement", "uniqueness_already_violated", 409,
        new Dictionary<string, object?> { ["type"] = type.ToWireString(), ["custom_label"] = customLabel });

    public static InstitutionalIdentifierError ConcurrencyConflict(string personId, IdentifierType type, int sequenceNumber, int expected, int actual) => new(
        "the identifier was modified since the version this request expected", "concurrency_conflict", 409,
        new Dictionary<string, object?>
        {
            ["person_id"] = personId, ["type"] = type.ToWireString(), ["sequence_number"] = sequenceNumber,
            ["expected_version"] = expected, ["actual_version"] = actual,
        });

    public static InstitutionalIdentifierError ActorRequired() => new(
        "X-Actor-Id is required to record who made this administrative change", "actor_required", 400);

    public static InstitutionalIdentifierError InvalidActorType(string actorType) => new(
        "X-Actor-Type must be one of user, service_account, device, system, anonymous, unknown", "invalid_actor_type", 400,
        new Dictionary<string, object?> { ["actor_type"] = actorType });

    public static InstitutionalIdentifierError Validation(IReadOnlyList<Api.ValidationErrorItem> errors) => new(
        "the request failed validation", "validation_failed", 422)
    { Errors = errors };

    public static InstitutionalIdentifierError AuditUnavailable() => new(
        "the audit sink failed; the change was not committed without an audit record", "audit_unavailable", 503);

    public static InstitutionalIdentifierError TenantContextMissing() => new(
        "tenant_context_missing: no tenant channel has resolved a context for this request", "tenant_context_missing", 400);

    /// <summary>Repository-boundary belt-and-braces check (dual-boundary discipline, BIO-DEV-001-QA-04 / BIO-IDN-001-QA-01).</summary>
    public static InstitutionalIdentifierError IdentityImmutable(string personId, IdentifierType type, int sequenceNumber) => new(
        $"an identifier's person_id, tenant_id, type, custom_label, sequence_number and value may never change once created: {personId}/{type.ToWireString()}/{sequenceNumber}",
        "identity_immutable", 500,
        new Dictionary<string, object?> { ["person_id"] = personId, ["type"] = type.ToWireString(), ["sequence_number"] = sequenceNumber });

    public static InstitutionalIdentifierError InvalidInitialState(string personId, IdentifierType type, int sequenceNumber, IdentifierStatus status, int version) => new(
        $"a newly created identifier must start ACTIVE at version 1: {personId}/{type.ToWireString()}/{sequenceNumber}", "invalid_initial_state", 500,
        new Dictionary<string, object?>
        {
            ["person_id"] = personId, ["type"] = type.ToWireString(), ["sequence_number"] = sequenceNumber,
            ["status"] = status.ToWireString(), ["version"] = version,
        });

    /// <summary>BIO-IDN-003's only governed transition: Active -&gt; Retired. Retired is terminal.</summary>
    public static InstitutionalIdentifierError AlreadyRetired(string personId, IdentifierType type, int sequenceNumber) => new(
        $"identifier is already retired: {personId}/{type.ToWireString()}/{sequenceNumber}", "already_retired", 409,
        new Dictionary<string, object?> { ["person_id"] = personId, ["type"] = type.ToWireString(), ["sequence_number"] = sequenceNumber });
}

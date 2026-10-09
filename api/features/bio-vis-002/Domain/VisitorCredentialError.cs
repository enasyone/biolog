namespace Biolog.Features.VisitorCredentials.Domain;

/// <summary>This feature's domain error, shaped identically to every sibling story's so it duck-types cleanly onto <see cref="Api.ProblemResponse"/>'s builder (ADR-0004 S2.1) - mirrors BIO-VIS-001's own <c>VisitorError</c>.</summary>
public sealed class VisitorCredentialError(string message, string code, int status, IReadOnlyDictionary<string, object?>? detail = null)
    : Exception(message)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
    public IReadOnlyDictionary<string, object?> Detail { get; } = detail ?? new Dictionary<string, object?>();

    public IReadOnlyList<Api.ValidationErrorItem>? Errors { get; init; }

    public static VisitorCredentialError ActorRequired() => new(
        "X-Actor-Id is required to record who requested this change", "actor_required", 400);

    public static VisitorCredentialError InvalidActorType(string actorType) => new(
        "X-Actor-Type must be one of user, service_account, device, system, anonymous, unknown", "invalid_actor_type", 400,
        new Dictionary<string, object?> { ["actor_type"] = actorType });

    /// <summary>ADR-0006 Decision 3: required only on the grant/revoke endpoints - the authorization decisions this feature makes.</summary>
    public static VisitorCredentialError ActorProvenanceRequired() => new(
        "this action requires a valid X-Actor-Provenance proof for the asserted X-Actor-Id (ADR-0006 Decision 3) - a server-computed HMAC bound to that specific actor id, distinct from the caller-asserted X-Actor-Id itself",
        "actor_provenance_required", 401);

    public static VisitorCredentialError Validation(IReadOnlyList<Api.ValidationErrorItem> errors) => new(
        "the request failed validation", "validation_failed", 422)
    { Errors = errors };

    public static VisitorCredentialError AuditUnavailable() => new(
        "the audit sink failed; the change was not committed without an audit record", "audit_unavailable", 503);

    public static VisitorCredentialError TenantContextMissing() => new(
        "tenant_context_missing: no tenant channel has resolved a context for this request", "tenant_context_missing", 400);

    public static VisitorCredentialError PermissionRequired(string permissionKey) => new(
        $"this action requires the calling actor to hold the '{permissionKey}' permission", "permission_required", 403,
        new Dictionary<string, object?> { ["permission_key"] = permissionKey });

    public static VisitorCredentialError VisitorNotFound(string visitorId) => new(
        $"visitor record not found: {visitorId}", "visitor_not_found", 404,
        new Dictionary<string, object?> { ["visitor_id"] = visitorId });

    public static VisitorCredentialError CredentialNotFound(string credentialId) => new(
        $"visitor credential not found: {credentialId}", "visitor_credential_not_found", 404,
        new Dictionary<string, object?> { ["credential_id"] = credentialId });

    public static VisitorCredentialError ConcurrencyConflict(string credentialId, int expected, int actual) => new(
        $"visitor credential '{credentialId}' was modified since the version this request expected", "concurrency_conflict", 409,
        new Dictionary<string, object?> { ["credential_id"] = credentialId, ["expected_version"] = expected, ["actual_version"] = actual });

    /// <summary>The disclosed boundary this story's README names explicitly: a visitor with no linked PERSON (a "lead", BIO-VIS-001's own deliberate design) can never have a real BIO-CRD-005 physical card issued against them, so a credential grant is impossible, not merely unimplemented.</summary>
    public static VisitorCredentialError VisitorHasNoLinkedPerson(string visitorId) => new(
        $"visitor '{visitorId}' has no linked person_id - a temporary credential backed by a real physical card requires one (BIO-CRD-005's own IssueAsync requires a real person_id); this visitor must be linked to a PERSON before a credential can be granted",
        "visitor_has_no_linked_person", 409,
        new Dictionary<string, object?> { ["visitor_id"] = visitorId });

    public static VisitorCredentialError GrantBlocked(string visitorId, string reasonCode, string message) => new(
        $"credential grant blocked for visitor '{visitorId}': {message}", reasonCode, 409,
        new Dictionary<string, object?> { ["visitor_id"] = visitorId });

    public static VisitorCredentialError AlreadyRevoked(string credentialId) => new(
        $"visitor credential '{credentialId}' is already revoked", "already_revoked", 409,
        new Dictionary<string, object?> { ["credential_id"] = credentialId });

    /// <summary>BIO-VIS-002-F01's own remediation: the repository's transition guard - any status change other than Pending-&gt;Granted, Pending-&gt;Failed or Granted-&gt;Revoked is a caller bug.</summary>
    public static VisitorCredentialError InvalidTransition(string credentialId, VisitorCredentialStatus from, VisitorCredentialStatus to) => new(
        $"visitor credential '{credentialId}' cannot move from '{from}' to '{to}'", "invalid_credential_transition", 500,
        new Dictionary<string, object?> { ["credential_id"] = credentialId, ["from"] = from.ToWireString(), ["to"] = to.ToWireString() });

    public static VisitorCredentialError ExpiresAtExceedsVisit(string visitorExpiresAt) => new(
        $"credential expires_at must not be later than the visit's own expires_at ({visitorExpiresAt})", "expires_at_exceeds_visit", 422,
        new Dictionary<string, object?> { ["visit_expires_at"] = visitorExpiresAt });

    /// <summary>
    /// BIO-VIS-002-F06's own remediation (Zainab round-4 FAIL): if this person's own opportunistic
    /// <see cref="VisitorCredentialService.ReconcileStrandedCardsAsync"/> attempt (run moments
    /// earlier in the same <see cref="VisitorCredentialService.GrantAsync"/> call) leaves ANY
    /// ledger entry still unresolved for this (person, card class), a new card must never be
    /// issued - a prior attempt's card may still be genuinely Active and unaccounted for, and
    /// issuing another would leave this one person holding two simultaneously Active physical-
    /// access credentials. Fail closed instead: the caller can retry once reconciliation (another
    /// grant, a checkout/revoke for this person, or the dedicated reconcile endpoint) succeeds.
    /// </summary>
    public static VisitorCredentialError StrandedCardUnresolved(string personId, string cardClass, IReadOnlyList<int> unresolvedSequenceNumbers) => new(
        $"person '{personId}' has {unresolvedSequenceNumbers.Count} unresolved stranded '{cardClass}' card(s) from a prior failed attempt that may still be genuinely active; a new credential cannot be granted until reconciliation resolves them",
        "stranded_card_unresolved", 409,
        new Dictionary<string, object?> { ["person_id"] = personId, ["card_class"] = cardClass, ["unresolved_card_sequence_numbers"] = unresolvedSequenceNumbers });
}

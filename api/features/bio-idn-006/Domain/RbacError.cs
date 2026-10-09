namespace Biolog.Features.Rbac.Domain;

/// <summary>
/// This feature's domain error, shaped identically to every sibling story's so it duck-types
/// cleanly onto <see cref="Api.ProblemResponse"/>'s builder (ADR-0004 §2.1).
/// </summary>
public sealed class RbacError(string message, string code, int status, IReadOnlyDictionary<string, object?>? detail = null)
    : Exception(message)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
    public IReadOnlyDictionary<string, object?> Detail { get; } = detail ?? new Dictionary<string, object?>();

    public IReadOnlyList<Api.ValidationErrorItem>? Errors { get; init; }

    /// <summary>Applying BIO-IDN-002-QA-01's lesson from the start - see IPersonExistenceGateway's header. Deliberately the same code/status/shape for missing and foreign-tenant (ADR-0001 §1.5, no enumeration oracle).</summary>
    public static RbacError PersonNotFound(string personId) => new(
        $"person not found in the resolved tenant: {personId}", "person_not_found", 404,
        new Dictionary<string, object?> { ["person_id"] = personId });

    public static RbacError ActorRequired() => new(
        "X-Actor-Id is required to record who made this administrative change", "actor_required", 400);

    public static RbacError InvalidActorType(string actorType) => new(
        "X-Actor-Type must be one of user, service_account, device, system, anonymous, unknown", "invalid_actor_type", 400,
        new Dictionary<string, object?> { ["actor_type"] = actorType });

    public static RbacError Validation(IReadOnlyList<Api.ValidationErrorItem> errors) => new(
        "the request failed validation", "validation_failed", 422)
    { Errors = errors };

    public static RbacError AuditUnavailable() => new(
        "the audit sink failed; the change was not committed without an audit record", "audit_unavailable", 503);

    public static RbacError TenantContextMissing() => new(
        "tenant_context_missing: no tenant channel has resolved a context for this request", "tenant_context_missing", 400);

    /// <summary>
    /// BIO-IDN-006-QA-08 (Zainab's security review, Critical; ADR-0006 Decision 3): the
    /// provenance gate on the four highest-risk mutation surfaces - see
    /// <see cref="Api.ActorProvenanceGate"/>'s own header. Identical for a missing proof and a
    /// wrong one, so a caller cannot distinguish the two.
    /// </summary>
    public static RbacError ActorProvenanceRequired() => new(
        "this action requires a valid X-Actor-Provenance proof for the asserted X-Actor-Id (ADR-0006 Decision 3) - a server-computed HMAC bound to that specific actor id, distinct from the caller-asserted X-Actor-Id itself",
        "actor_provenance_required", 401);

    // --- PersonRoleAssignment ---

    public static RbacError RoleAssignmentNotFound(string personId, RoleType role) => new(
        $"person does not hold this role: {personId}/{role.ToWireString()}", "role_assignment_not_found", 404,
        new Dictionary<string, object?> { ["person_id"] = personId, ["role"] = role.ToWireString() });

    public static RbacError RoleAssignmentConcurrencyConflict(string personId, RoleType role, int expected, int actual) => new(
        "the role assignment was modified since the version this request expected", "concurrency_conflict", 409,
        new Dictionary<string, object?> { ["person_id"] = personId, ["role"] = role.ToWireString(), ["expected_version"] = expected, ["actual_version"] = actual });

    /// <summary>
    /// BIO-IDN-006-QA-01 (Seyi round 1, Critical): only an actor who ALREADY holds an active
    /// SUPER_ADMIN assignment may grant or revoke SUPER_ADMIN for anyone - no bootstrap
    /// exception. See <see cref="RoleAssignmentService"/>'s header for why this is deliberately
    /// absolute, and docs/features/bio-idn-006/README.md for the disclosed consequence (a
    /// tenant's very first SUPER_ADMIN cannot be created through this endpoint at all).
    /// </summary>
    public static RbacError SuperAdminMutationRequiresSuperAdmin() => new(
        "assigning or revoking SUPER_ADMIN requires the calling actor to already hold an active SUPER_ADMIN role",
        "super_admin_mutation_requires_super_admin", 403);

    /// <summary>BIO-IDN-006-QA-02 (Seyi round 1, High): a sensitive action's own maker-checker clearance, real and enforced - see <see cref="RoleAssignmentService"/>'s header.</summary>
    public static RbacError SecondApprovalRequired(string actionKey, string targetRef) => new(
        $"this action requires an approved maker-checker request before it may proceed: {actionKey}/{targetRef}", "second_approval_required", 403,
        new Dictionary<string, object?> { ["action_key"] = actionKey, ["target_ref"] = targetRef });

    /// <summary>BIO-IDN-006-QA-03/QA-04 (Seyi round 2, Critical): the general authorization gate on role assignment, permission-grant configuration and sensitive-action-rule configuration - see PermissionService.ActorHasPermissionAsync.</summary>
    public static RbacError PermissionRequired(string permissionKey) => new(
        $"this action requires the calling actor to hold the '{permissionKey}' permission", "permission_required", 403,
        new Dictionary<string, object?> { ["permission_key"] = permissionKey });

    public static RbacError RoleAssignmentIdentityImmutable(string personId, RoleType role) => new(
        $"a role assignment's tenant_id, person_id and role may never change once created: {personId}/{role.ToWireString()}", "identity_immutable", 500,
        new Dictionary<string, object?> { ["person_id"] = personId, ["role"] = role.ToWireString() });

    public static RbacError RoleAssignmentInvalidInitialState(string personId, RoleType role, RoleAssignmentStatus status, int version) => new(
        $"a newly created role assignment must start ACTIVE at version 1: {personId}/{role.ToWireString()}", "invalid_initial_state", 500,
        new Dictionary<string, object?> { ["person_id"] = personId, ["role"] = role.ToWireString(), ["status"] = status.ToWireString(), ["version"] = version });

    // --- RolePermissionGrant ---

    /// <summary>Super Admin's authority is never configurable - see RoleType's and RolePermissionGrant's headers. This is the enforcement point.</summary>
    public static RbacError SuperAdminGrantNotConfigurable() => new(
        "SUPER_ADMIN implicitly holds every permission and is never configurable via a grant record", "super_admin_grant_not_configurable", 422);

    // --- ApprovalRequest / maker-checker ---

    public static RbacError ApprovalRequestNotFound(string actionKey, string targetRef, int sequenceNumber) => new(
        $"approval request not found: {actionKey}/{targetRef}/{sequenceNumber}", "approval_request_not_found", 404,
        new Dictionary<string, object?> { ["action_key"] = actionKey, ["target_ref"] = targetRef, ["sequence_number"] = sequenceNumber });

    public static RbacError ApprovalRequestConcurrencyConflict(string actionKey, string targetRef, int sequenceNumber, int expected, int actual) => new(
        "the approval request was modified since the version this request expected", "concurrency_conflict", 409,
        new Dictionary<string, object?>
        {
            ["action_key"] = actionKey, ["target_ref"] = targetRef, ["sequence_number"] = sequenceNumber,
            ["expected_version"] = expected, ["actual_version"] = actual,
        });

    /// <summary>AC03's real structural enforcement point for "at most one pending request per target": a second PENDING request for the same (tenant, action_key, target_ref) may never coexist with the first.</summary>
    public static RbacError ApprovalRequestAlreadyPending(string actionKey, string targetRef) => new(
        $"a pending approval request already exists for this target: {actionKey}/{targetRef}", "approval_request_already_pending", 409,
        new Dictionary<string, object?> { ["action_key"] = actionKey, ["target_ref"] = targetRef });

    /// <summary>Decided requests are terminal - see ApprovalRequestStatus's header.</summary>
    public static RbacError ApprovalRequestAlreadyDecided(string actionKey, string targetRef, int sequenceNumber, ApprovalRequestStatus status) => new(
        $"this approval request already has a decision recorded: {actionKey}/{targetRef}/{sequenceNumber}", "approval_request_already_decided", 409,
        new Dictionary<string, object?>
        {
            ["action_key"] = actionKey, ["target_ref"] = targetRef, ["sequence_number"] = sequenceNumber, ["status"] = status.ToWireString(),
        });

    /// <summary>
    /// AC03's core security property and this story's mandatory privilege-escalation test
    /// target: the checker may never be the same actor as the maker - see
    /// <see cref="ApprovalRequest"/>'s header "Separation of duties".
    /// </summary>
    public static RbacError SelfApprovalForbidden() => new(
        "the actor who requested this approval may not also decide it (maker-checker separation of duties)", "self_approval_forbidden", 403);

    public static RbacError ApprovalRequestIdentityImmutable(string actionKey, string targetRef, int sequenceNumber) => new(
        $"an approval request's tenant_id, action_key, target_ref and sequence_number may never change once created: {actionKey}/{targetRef}/{sequenceNumber}",
        "identity_immutable", 500,
        new Dictionary<string, object?> { ["action_key"] = actionKey, ["target_ref"] = targetRef, ["sequence_number"] = sequenceNumber });

    public static RbacError ApprovalRequestInvalidInitialState(string actionKey, string targetRef, int sequenceNumber, ApprovalRequestStatus status, int version) => new(
        $"a newly created approval request must start PENDING at version 1: {actionKey}/{targetRef}/{sequenceNumber}", "invalid_initial_state", 500,
        new Dictionary<string, object?>
        {
            ["action_key"] = actionKey, ["target_ref"] = targetRef, ["sequence_number"] = sequenceNumber, ["status"] = status.ToWireString(), ["version"] = version,
        });

    /// <summary>BIO-IDN-006-QA-05 (Seyi round 2, High): an approval is single-use - see ApprovalRequest's header "Consumption is single-use".</summary>
    public static RbacError ApprovalAlreadyConsumed(string actionKey, string targetRef, int sequenceNumber) => new(
        $"this approval was already consumed by an earlier mutation: {actionKey}/{targetRef}/{sequenceNumber}", "approval_already_consumed", 409,
        new Dictionary<string, object?> { ["action_key"] = actionKey, ["target_ref"] = targetRef, ["sequence_number"] = sequenceNumber });

    /// <summary>Repository-boundary belt-and-braces check: only an APPROVED request may ever be consumed - unreachable via any legitimate call, since MakerCheckerService.TryConsumeApprovalAsync only ever attempts this on a record it already confirmed is Approved and unconsumed.</summary>
    public static RbacError ApprovalNotApproved(string actionKey, string targetRef, int sequenceNumber, ApprovalRequestStatus status) => new(
        $"only an APPROVED request may be consumed: {actionKey}/{targetRef}/{sequenceNumber}", "approval_not_approved", 500,
        new Dictionary<string, object?> { ["action_key"] = actionKey, ["target_ref"] = targetRef, ["sequence_number"] = sequenceNumber, ["status"] = status.ToWireString() });
}

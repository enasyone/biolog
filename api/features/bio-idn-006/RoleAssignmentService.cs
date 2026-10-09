using Biolog.Features.Rbac.Api;
using Biolog.Features.Rbac.Audit;
using Biolog.Features.Rbac.Domain;
using Biolog.Features.Rbac.Identifiers;
using Biolog.Features.Rbac.Tenancy;

namespace Biolog.Features.Rbac;

/// <summary>
/// Application service for BIO-IDN-006-AC01/AC02/AC04: assigning and revoking
/// <see cref="RoleType"/>s to a PERSON within the resolved tenant. Implements ADR-0003 Decision
/// 7's audit-atomicity pattern exactly as established across every sibling story in this epic:
/// a per-<c>(PersonId, Role)</c> lock (<see cref="RoleAssignmentLockRegistry"/>), audit-before-
/// commit, <see cref="IRbacAuditSink.ExistsAsync"/> verification on an ambiguous audit-write
/// throw, <see cref="CancellationToken.None"/> on the commit once the audit write is confirmed.
///
/// <b>AC01/AC02 - a role held in one tenant grants nothing in another.</b> Every method reads
/// the resolved tenant itself, never a caller-supplied one - AC02's tenant-scoping starts at the
/// assignment, before <see cref="PermissionService"/> is ever consulted.
///
/// <b>AC04 - role changes are audited, and the assignment itself is never deleted.</b>
/// <see cref="RevokeAsync"/> is a governed transition (Active -&gt; Revoked, terminal until
/// re-assigned), never a delete - see <see cref="PersonRoleAssignment"/>'s header.
///
/// <b>Applying BIO-IDN-002-QA-01 from the start.</b> <see cref="AssignAsync"/> proves
/// <paramref name="personId"/>'s EXISTENCE in the resolved tenant through
/// <see cref="IPersonExistenceGateway"/> before the lock is even acquired and before any audit
/// write - see that interface's header.
///
/// <b>BIO-IDN-006-QA-01 (Seyi round 1, Critical).</b> The round 1 submission let ANY asserted
/// actor grant themselves (or anyone) <see cref="RoleType.SuperAdmin"/> - the mandatory
/// privilege-escalation negative case the story's own security criteria demand. The review
/// correctly rejected the "no real authentication exists yet, so no real authorization is
/// possible either" framing this story had used to justify deferring its own endpoints'
/// enforcement: authorization (does THIS asserted actor already hold the authority it claims)
/// is a genuinely separable concern from authentication (is the asserted identity real), and
/// this story already ships the mechanism to check the former - it just never called it.
///
/// The fix is deliberately ABSOLUTE, with no bootstrap exception: mutating
/// <see cref="RoleType.SuperAdmin"/> (assign OR revoke, symmetrically - stripping a legitimate
/// admin's access without authority is just as much a security concern as granting unauthorized
/// access) requires the calling actor to ALREADY hold an active <c>SUPER_ADMIN</c> assignment,
/// looked up directly against this same repository by <see cref="Audit.AuditActor.Id"/> (which
/// must itself be a well-formed canonical <c>per_</c> id for this specific check - the free-form
/// actor ids this codebase's disclosed pre-auth convention otherwise allows, like
/// <c>"admin_alice"</c>, can never satisfy it). A tenant's very first <c>SUPER_ADMIN</c> can
/// therefore never be created through this endpoint at all - a deliberate, disclosed
/// consequence, not an oversight: provisioning a tenant's first administrator is an out-of-band
/// operation (seeding the repository directly), the same way real production RBAC systems solve
/// this exact bootstrap problem, not something the ordinary self-service API should be able to
/// do for itself.
///
/// <b>BIO-IDN-006-QA-02 (Seyi round 1, High).</b> <see cref="MakerCheckerService.IsActionClearedAsync"/>
/// existed but had no real production consumer - "the story supplies a
/// permission verification service and the actor seam, but never connects them" applied
/// equally to maker-checker. The fix pairs naturally with QA-01: mutating <c>SUPER_ADMIN</c> -
/// arguably this whole system's single most sensitive operation - is now this story's own OWNED
/// sensitive action, gated by <see cref="MakerCheckerService.IsActionClearedAsync"/> using action
/// keys <c>"rbac.super_admin_assign"</c>/<c>"rbac.super_admin_revoke"</c> and the target
/// <paramref name="personId"/> as <c>TargetRef</c>. By default (no <c>SensitiveActionRule</c>
/// configured) this clears immediately - fail-OPEN, per that type's own default - so a tenant
/// that never opts in sees no behavioural change beyond QA-01's own SUPER_ADMIN-authorization
/// gate; a tenant that DOES opt in genuinely cannot mutate SUPER_ADMIN without an approved
/// maker-checker request for that specific target, proving the hook is real, not inert.
///
/// Both gates run BEFORE anything else in <see cref="AssignAsync"/>/<see cref="RevokeAsync"/> -
/// before format validation, before the existence gateway, before the lock - an unauthorized or
/// unapproved caller learns nothing about the target's state.
///
/// <b>BIO-IDN-006-QA-03 (Seyi round 2, Critical).</b> Round 1's fix protected only the
/// <see cref="RoleType.SuperAdmin"/> enum value - any OTHER role (e.g. <c>TENANT_ADMIN</c>)
/// could still be assigned by an arbitrary asserted actor, who could then use
/// <see cref="PermissionService.SetGrantAsync"/> to grant that role any permission key at all
/// (that gap is fixed separately - see <see cref="PermissionService"/>'s header). Since
/// permission keys are open-ended, a non-`SUPER_ADMIN` role configured this way is not a "safe,
/// lesser" privilege - it is the identical escalation, one hop removed. Every role
/// assignment/revocation now requires the caller to hold <see cref="RoleAssignPermission"/>/
/// <see cref="RoleRevokePermission"/> (checked via <see cref="PermissionService.ActorHasPermissionAsync"/>
/// - the SAME general mechanism <c>SUPER_ADMIN</c>'s own absolute gate deliberately does NOT use,
/// since that one role's authority must never be delegable via a configurable grant at all).
/// Nobody holds either permission by default (fail-closed, per <see cref="RolePermissionGrant"/>'s
/// own default) except <c>SUPER_ADMIN</c> itself (implicit, per <see cref="RoleType"/>'s header) -
/// so in practice, only a `SUPER_ADMIN` or a role it has explicitly delegated this permission to
/// may assign or revoke any OTHER role, inheriting `SUPER_ADMIN`'s own already-solved bootstrap
/// story rather than needing a second one.
///
/// <b>BIO-IDN-006-QA-05 (Seyi round 2, High).</b> The `SUPER_ADMIN` maker-checker gate now calls
/// <see cref="MakerCheckerService.TryConsumeApprovalAsync"/> (atomic, single-use) instead of the
/// read-only <see cref="MakerCheckerService.IsActionClearedAsync"/> - see
/// <see cref="ApprovalRequest"/>'s header "Consumption is single-use" for why a permanently
/// reusable approval was itself a privilege-escalation-adjacent bug.
///
/// <b>BIO-IDN-006-QA-06 (Seyi round 3, High).</b> QA-05's own fix consumed the approval from
/// <see cref="RequireSuperAdminMutationAuthorityAsync"/>, at the very top of
/// <see cref="AssignAsync"/>/<see cref="RevokeAsync"/> - before person-id format validation, the
/// existence gateway, the idempotent no-op check, and the audit write. A target that turned out
/// malformed or nonexistent, a stale concurrency-conflict retry, a transient audit-sink outage,
/// or simply a mutation that was already a no-op each independently burned a genuinely valid
/// approval for a change that never actually happened - "failed or retried sensitive mutations
/// can permanently burn a valid approval." <see cref="RequireSuperAdminMutationAuthorityAsync"/>
/// now only performs a READ-ONLY <see cref="MakerCheckerService.IsActionClearedAsync"/> check (so
/// the "authorized-but-unapproved caller learns nothing" ordering property is unchanged);
/// <see cref="ConsumeSuperAdminApprovalAsync"/> performs the actual, atomic consumption, called
/// only once a real mutation is about to commit - after every validation step above, AND after
/// the audit write is confirmed durable, so an audit-sink outage never burns the approval either.
/// </summary>
internal sealed class RoleAssignmentService(
    ICurrentTenantProvider tenantProvider,
    IPersonRoleAssignmentRepository repository,
    IRbacAuditSink auditSink,
    RoleAssignmentLockRegistry lockRegistry,
    IPersonExistenceGateway personGateway,
    MakerCheckerService makerCheckerService,
    PermissionService permissionService)
{
    private const string SuperAdminAssignActionKey = "rbac.super_admin_assign";
    private const string SuperAdminRevokeActionKey = "rbac.super_admin_revoke";

    /// <summary>BIO-IDN-006-QA-03: required to assign any role OTHER than SUPER_ADMIN (which keeps its own separate, absolute gate).</summary>
    public const string RoleAssignPermission = "rbac.role_assign";

    /// <summary>BIO-IDN-006-QA-03: required to revoke any role OTHER than SUPER_ADMIN.</summary>
    public const string RoleRevokePermission = "rbac.role_revoke";

    /// <summary>Idempotent: assigning a role the person already actively holds is a no-op (nothing changed, nothing to audit). Reassigning a REVOKED role reactivates the same record.</summary>
    public async Task<PersonRoleAssignment> AssignAsync(
        string personId, RoleType role, AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();

        if (role == RoleType.SuperAdmin)
        {
            await RequireSuperAdminMutationAuthorityAsync(tenantId, actor, SuperAdminAssignActionKey, personId, cancellationToken);
        }
        else
        {
            await RequirePermissionAsync(tenantId, actor, RoleAssignPermission, cancellationToken);
        }

        if (!CanonicalId.IsPersonId(personId))
        {
            throw RbacError.Validation([new ValidationErrorItem("person_id", "invalid_format", "person_id must be a well-formed canonical per_ identifier")]);
        }

        if (!await personGateway.ExistsAsync(personId, cancellationToken))
        {
            throw RbacError.PersonNotFound(personId);
        }

        using var _ = await lockRegistry.AcquireAsync(tenantId, personId, role, cancellationToken);
        var current = await repository.GetAsync(tenantId, personId, role, cancellationToken);

        if (current is null)
        {
            var now = UtcInstant.Now();
            var assignment = new PersonRoleAssignment(tenantId, personId, role) { Status = RoleAssignmentStatus.Active, Version = 1, CreatedAt = now, UpdatedAt = now };

            await AuditAsync(tenantId, actor, "rbac.role_assign", personId, role, "role_assigned", correlationId, cancellationToken);
            if (role == RoleType.SuperAdmin)
            {
                await ConsumeSuperAdminApprovalAsync(SuperAdminAssignActionKey, personId);
            }

            return await repository.CreateAsync(assignment, CancellationToken.None);
        }

        if (current.Status == RoleAssignmentStatus.Active)
        {
            return current; // no-op: already active, nothing to audit, nothing to consume
        }

        var reactivated = current with { Status = RoleAssignmentStatus.Active, RevokedAt = null, Version = current.Version + 1, UpdatedAt = UtcInstant.Now() };

        await AuditAsync(tenantId, actor, "rbac.role_assign", personId, role, "role_reassigned", correlationId, cancellationToken);
        if (role == RoleType.SuperAdmin)
        {
            await ConsumeSuperAdminApprovalAsync(SuperAdminAssignActionKey, personId);
        }

        return await repository.ReplaceAsync(current, reactivated, CancellationToken.None);
    }

    /// <summary>Idempotent no-op if already revoked. Requires the current version so a caller cannot revoke against a stale assumption of the assignment's state.</summary>
    public async Task<PersonRoleAssignment> RevokeAsync(
        string personId, RoleType role, RevokeRoleRequest request, AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();

        if (role == RoleType.SuperAdmin)
        {
            await RequireSuperAdminMutationAuthorityAsync(tenantId, actor, SuperAdminRevokeActionKey, personId, cancellationToken);
        }
        else
        {
            await RequirePermissionAsync(tenantId, actor, RoleRevokePermission, cancellationToken);
        }

        var errors = new List<ValidationErrorItem>();
        if (!CanonicalId.IsPersonId(personId))
        {
            errors.Add(new ValidationErrorItem("person_id", "invalid_format", "person_id must be a well-formed canonical per_ identifier"));
        }

        if (request.Version is not int expectedVersion)
        {
            errors.Add(new ValidationErrorItem("version", "required", "version is required so a stale write can be detected"));
            expectedVersion = default;
        }

        ThrowIfInvalid(errors);

        using var _ = await lockRegistry.AcquireAsync(tenantId, personId, role, cancellationToken);
        var current = await repository.GetAsync(tenantId, personId, role, cancellationToken) ?? throw RbacError.RoleAssignmentNotFound(personId, role);

        if (current.Status == RoleAssignmentStatus.Revoked)
        {
            return current; // no-op: already revoked, nothing to audit, nothing to consume
        }

        if (expectedVersion != current.Version)
        {
            throw RbacError.RoleAssignmentConcurrencyConflict(personId, role, expectedVersion, current.Version);
        }

        var now = UtcInstant.Now();
        var revoked = current with { Status = RoleAssignmentStatus.Revoked, RevokedAt = now, Version = current.Version + 1, UpdatedAt = now };

        await AuditAsync(tenantId, actor, "rbac.role_revoke", personId, role, "role_revoked", correlationId, cancellationToken);
        if (role == RoleType.SuperAdmin)
        {
            await ConsumeSuperAdminApprovalAsync(SuperAdminRevokeActionKey, personId);
        }

        return await repository.ReplaceAsync(current, revoked, CancellationToken.None);
    }

    /// <summary>AC04's audit-visible history: every role this person holds or has ever held, tenant-scoped.</summary>
    public Task<IReadOnlyList<PersonRoleAssignment>> ListForPersonAsync(string personId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        return repository.ListForPersonAsync(tenantId, personId, cancellationToken);
    }

    private async Task AuditAsync(
        string tenantId, AuditActor actor, string action, string personId, RoleType role, string reasonCode, string correlationId, CancellationToken cancellationToken)
    {
        var target = new AuditTarget("person_role_assignment", $"{personId}/{role.ToWireString()}");
        var detail = new Dictionary<string, (object?, string)> { ["person_id"] = (personId, "internal"), ["role"] = (role.ToWireString(), "internal") };
        var auditEvent = RbacAuditEventBuilder.Build(tenantId, actor, action, target, AuditOutcome.Allowed, reasonCode, correlationId, causationId: null, detail, withheld: []);

        try
        {
            await auditSink.WriteAsync(auditEvent, cancellationToken);
            return; // confirmed: the write completed without throwing
        }
        catch (Exception)
        {
            // Ambiguous - fall through to verify rather than assume failure.
        }

        bool exists;
        try
        {
            exists = await auditSink.ExistsAsync(auditEvent.EventId, cancellationToken);
        }
        catch (Exception)
        {
            throw RbacError.AuditUnavailable(); // total outage: cannot even verify
        }

        if (!exists)
        {
            throw RbacError.AuditUnavailable(); // confirmed: genuinely never persisted
        }

        // else: confirmed durably present despite the throw on WriteAsync - proceed as success.
    }

    /// <summary>
    /// BIO-IDN-006-QA-01/QA-02/QA-05's combined fix: proves the caller already holds SUPER_ADMIN,
    /// then atomically CONSUMES maker-checker clearance for this specific mutation (never merely
    /// checks it - see ApprovalRequest's header "Consumption is single-use") - both BEFORE
    /// anything else in the calling method runs. Order matters here specifically: the authority
    /// check runs first so an actor with no SUPER_ADMIN authority at all learns nothing about
    /// whether a maker-checker rule is even configured for this action.
    /// </summary>
    private async Task RequireSuperAdminMutationAuthorityAsync(
        string tenantId, AuditActor actor, string actionKey, string targetPersonId, CancellationToken cancellationToken)
    {
        if (!await ActorHoldsActiveSuperAdminAsync(tenantId, actor, cancellationToken))
        {
            throw RbacError.SuperAdminMutationRequiresSuperAdmin();
        }

        // BIO-IDN-006-QA-06: a READ-ONLY check here, deliberately never consuming - see
        // ConsumeSuperAdminApprovalAsync's own header for why the actual consumption is deferred
        // to immediately before the commit, not performed this early.
        if (!await makerCheckerService.IsActionClearedAsync(actionKey, targetPersonId, cancellationToken))
        {
            throw RbacError.SecondApprovalRequired(actionKey, targetPersonId);
        }
    }

    /// <summary>
    /// BIO-IDN-006-QA-06 (Seyi round 3, High). Round 2's fix called
    /// <see cref="MakerCheckerService.TryConsumeApprovalAsync"/> from
    /// <see cref="RequireSuperAdminMutationAuthorityAsync"/>, at the very top of
    /// <see cref="AssignAsync"/>/<see cref="RevokeAsync"/> - BEFORE person-id format validation,
    /// the existence gateway, the idempotent no-op check, and the audit write. Any one of those
    /// failing afterward (a malformed or nonexistent target, a caller retrying a stale
    /// concurrency conflict, a transient audit-sink outage, or simply the mutation already being
    /// a no-op) permanently burned a genuinely valid approval for a mutation that never actually
    /// happened, with no way to recover it - a legitimate maker/checker pair would have to raise
    /// and re-approve an entirely new request just to retry the SAME already-approved change.
    ///
    /// Fix: consumption now happens here, called only once a REAL mutation is about to commit -
    /// after format validation, the existence check, the lock, the no-op check (a no-op consumes
    /// nothing, mirroring "nothing to audit" for the same case), and AFTER the audit write has
    /// been confirmed durable (so an audit-sink outage never burns the approval either - the
    /// operation fails closed with the approval untouched, safely retryable). Only the actual
    /// repository commit remains after this call, using the same <see cref="CancellationToken.None"/>
    /// discipline as the commit itself (ADR-0003 Decision 7 element 4) - a cancellation in this
    /// narrow window must not leave the approval consumed with nothing committed.
    /// </summary>
    private async Task ConsumeSuperAdminApprovalAsync(string actionKey, string targetPersonId)
    {
        if (!await makerCheckerService.TryConsumeApprovalAsync(actionKey, targetPersonId, CancellationToken.None))
        {
            throw RbacError.SecondApprovalRequired(actionKey, targetPersonId);
        }
    }

    /// <summary>BIO-IDN-006-QA-03: the general authorization gate for every role OTHER than SUPER_ADMIN - see class header.</summary>
    private async Task RequirePermissionAsync(string tenantId, AuditActor actor, string permissionKey, CancellationToken cancellationToken)
    {
        if (!await permissionService.ActorHasPermissionAsync(tenantId, actor.Id, permissionKey, cancellationToken))
        {
            throw RbacError.PermissionRequired(permissionKey);
        }
    }

    /// <summary>
    /// The actor's own id must itself be a well-formed canonical <c>per_</c> id to be looked up
    /// here at all - the free-form actor ids this codebase's disclosed pre-auth convention
    /// otherwise allows (e.g. <c>"admin_alice"</c>) can never satisfy this check, by
    /// construction (see class header "BIO-IDN-006-QA-01").
    /// </summary>
    private async Task<bool> ActorHoldsActiveSuperAdminAsync(string tenantId, AuditActor actor, CancellationToken cancellationToken)
    {
        if (!CanonicalId.IsPersonId(actor.Id))
        {
            return false;
        }

        var assignment = await repository.GetAsync(tenantId, actor.Id, RoleType.SuperAdmin, cancellationToken);
        return assignment?.Status == RoleAssignmentStatus.Active;
    }

    private string RequireTenant() => tenantProvider.TenantId ?? throw RbacError.TenantContextMissing();

    private static void ThrowIfInvalid(List<ValidationErrorItem> errors)
    {
        if (errors.Count > 0)
        {
            throw RbacError.Validation(errors);
        }
    }
}

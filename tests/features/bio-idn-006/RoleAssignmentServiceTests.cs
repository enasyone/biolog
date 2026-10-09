using Biolog.Features.Rbac.Api;
using Biolog.Features.Rbac.Audit;
using Biolog.Features.Rbac.Domain;
using Biolog.Features.Rbac.Identifiers;
using Xunit;

namespace Biolog.Features.Rbac.Tests;

/// <summary>BIO-IDN-006-AC01/AC02/AC04: assigning/revoking roles, and ADR-0003 Decision 7's audit-atomicity pattern applied to that mutation.</summary>
public sealed class RoleAssignmentServiceTests
{
    private const string TenantId = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77";
    private const string TenantB = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a88";
    private const string PersonId = "per_0199e0c1-9d40-7e52-b103-7c9a4e2f6b1d";

    /// <summary>
    /// BIO-IDN-006-QA-03: every role assignment now requires the caller to hold real authority -
    /// this shared default Actor is a pre-seeded, already-privileged SUPER_ADMIN (see
    /// NewService's own seeding), not a free-form pre-auth id like this codebase's usual
    /// "admin_alice" convention. Tests that specifically probe UNAUTHORIZED behaviour construct
    /// their own separate, deliberately-unprivileged actor instead (see the PRIVILEGE_ESCALATION
    /// tests below) - this default exists purely so every test NOT about authorization itself
    /// doesn't need its own bootstrap boilerplate.
    /// </summary>
    private const string BootstrapAdminPersonId = "per_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a99";
    private static readonly AuditActor Actor = new(ActorType.User, BootstrapAdminPersonId);

    private static async Task<RoleAssignmentService> NewService(
        IPersonRoleAssignmentRepository? repository = null, IRbacAuditSink? auditSink = null, RoleAssignmentLockRegistry? lockRegistry = null,
        IPersonExistenceGateway? personGateway = null, string tenantId = TenantId, MakerCheckerService? makerCheckerService = null,
        PermissionService? permissionService = null, bool seedBootstrapAdmin = true, CancellationToken cancellationToken = default)
    {
        var repo = repository ?? new InMemoryPersonRoleAssignmentRepository();
        var gateway = personGateway ?? new FakePersonExistenceGateway();
        var sink = auditSink ?? new InMemoryRbacAuditSink();
        var tenantProvider = new FakeCurrentTenantProvider(tenantId);

        if (seedBootstrapAdmin && await repo.GetAsync(tenantId, BootstrapAdminPersonId, RoleType.SuperAdmin, cancellationToken) is null)
        {
            await SeedBootstrapSuperAdminAsync(repo, tenantId, BootstrapAdminPersonId, cancellationToken);
        }

        var permissions = permissionService ?? new PermissionService(
            tenantProvider, new InMemoryRolePermissionGrantRepository(), repo, sink, new PermissionGrantLockRegistry(), gateway);

        return new(
            tenantProvider, repo, sink, lockRegistry ?? new RoleAssignmentLockRegistry(), gateway,
            makerCheckerService ?? NewMakerCheckerService(tenantId, repo, permissions), permissions);
    }

    /// <summary>
    /// A fresh MakerCheckerService with no SensitiveActionRule ever configured - IsActionClearedAsync's own
    /// fail-open default means this always clears, so tests that do not care about maker-checker at all are
    /// unaffected. Its internal permission gate (BIO-IDN-006-QA-04) resolves against
    /// <paramref name="assignmentRepository"/> when given, so a caller that has already seeded a bootstrap
    /// SUPER_ADMIN into that same repository can also configure sensitive-action rules through it.
    /// </summary>
    internal static MakerCheckerService NewMakerCheckerService(
        string tenantId, IPersonRoleAssignmentRepository? assignmentRepository = null, PermissionService? permissionService = null)
    {
        var tenantProvider = new FakeCurrentTenantProvider(tenantId);
        var assignments = assignmentRepository ?? new InMemoryPersonRoleAssignmentRepository();
        var permissions = permissionService ?? new PermissionService(
            tenantProvider, new InMemoryRolePermissionGrantRepository(), assignments, new InMemoryRbacAuditSink(), new PermissionGrantLockRegistry(), new FakePersonExistenceGateway());
        return new(
            tenantProvider, new InMemorySensitiveActionRuleRepository(), new InMemoryApprovalRequestRepository(),
            new InMemoryRbacAuditSink(), new SensitiveActionRuleLockRegistry(), new ApprovalRequestLockRegistry(), permissions);
    }

    /// <summary>Seeds a bootstrap SUPER_ADMIN directly via the repository (bypassing the service - the same out-of-band path production tenant provisioning would use, see RoleAssignmentService's own header) so tests can exercise SUPER_ADMIN mutations as an already-privileged actor.</summary>
    internal static async Task<AuditActor> SeedBootstrapSuperAdminAsync(IPersonRoleAssignmentRepository repository, string tenantId, string bootstrapAdminPersonId, CancellationToken ct)
    {
        var now = UtcInstant.Now();
        await repository.CreateAsync(
            new PersonRoleAssignment(tenantId, bootstrapAdminPersonId, RoleType.SuperAdmin) { Status = RoleAssignmentStatus.Active, Version = 1, CreatedAt = now, UpdatedAt = now }, ct);
        return new AuditActor(ActorType.User, bootstrapAdminPersonId);
    }

    [Fact]
    public async Task AC01_assigning_a_baseline_role_creates_an_active_assignment_at_version_1()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = await NewService();

        var assignment = await service.AssignAsync(PersonId, RoleType.Enrolment, Actor, CanonicalId.NewEventId(), ct);

        Assert.Equal(RoleAssignmentStatus.Active, assignment.Status);
        Assert.Equal(1, assignment.Version);
        Assert.Equal(RoleType.Enrolment, assignment.Role);
    }

    [Fact]
    public async Task AC01_a_person_can_hold_several_roles_at_once()
    {
        var ct = TestContext.Current.CancellationToken;
        var repository = new InMemoryPersonRoleAssignmentRepository();
        var service = await NewService(repository);
        await service.AssignAsync(PersonId, RoleType.Enrolment, Actor, CanonicalId.NewEventId(), ct);
        await service.AssignAsync(PersonId, RoleType.Biometric, Actor, CanonicalId.NewEventId(), ct);

        var roles = await service.ListForPersonAsync(PersonId, ct);

        Assert.Equal(2, roles.Count(r => r.Status == RoleAssignmentStatus.Active));
    }

    [Fact]
    public async Task Assigning_an_already_active_role_is_a_no_op()
    {
        var ct = TestContext.Current.CancellationToken;
        var sink = new InMemoryRbacAuditSink();
        var service = await NewService(auditSink: sink);
        var first = await service.AssignAsync(PersonId, RoleType.Enrolment, Actor, CanonicalId.NewEventId(), ct);
        var eventsAfterFirst = sink.Events.Count;

        var second = await service.AssignAsync(PersonId, RoleType.Enrolment, Actor, CanonicalId.NewEventId(), ct);

        Assert.Equal(first.Version, second.Version);
        Assert.Equal(eventsAfterFirst, sink.Events.Count); // no-op: nothing new to audit
    }

    [Fact]
    public async Task Revoking_a_role_moves_it_to_REVOKED_and_preserves_the_record()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = await NewService();
        var assigned = await service.AssignAsync(PersonId, RoleType.Enrolment, Actor, CanonicalId.NewEventId(), ct);

        var revoked = await service.RevokeAsync(
            PersonId, RoleType.Enrolment, new RevokeRoleRequest { Version = assigned.Version }, Actor, CanonicalId.NewEventId(), ct);

        Assert.Equal(RoleAssignmentStatus.Revoked, revoked.Status);
        Assert.NotNull(revoked.RevokedAt);
        var stillOnFile = (await service.ListForPersonAsync(PersonId, ct)).Single(r => r.Role == RoleType.Enrolment);
        Assert.Equal(RoleAssignmentStatus.Revoked, stillOnFile.Status); // AC04: preserved, not deleted
    }

    [Fact]
    public async Task Re_assigning_a_revoked_role_reactivates_the_same_record()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = await NewService();
        var assigned = await service.AssignAsync(PersonId, RoleType.Enrolment, Actor, CanonicalId.NewEventId(), ct);
        await service.RevokeAsync(PersonId, RoleType.Enrolment, new RevokeRoleRequest { Version = assigned.Version }, Actor, CanonicalId.NewEventId(), ct);

        var reactivated = await service.AssignAsync(PersonId, RoleType.Enrolment, Actor, CanonicalId.NewEventId(), ct);

        Assert.Equal(RoleAssignmentStatus.Active, reactivated.Status);
        Assert.Null(reactivated.RevokedAt);
        Assert.Equal(3, reactivated.Version); // 1 (assign) -> 2 (revoke) -> 3 (reassign) - one record, full history via version
    }

    [Fact]
    public async Task Revoking_an_already_revoked_role_is_a_no_op()
    {
        var ct = TestContext.Current.CancellationToken;
        var sink = new InMemoryRbacAuditSink();
        var service = await NewService(auditSink: sink);
        var assigned = await service.AssignAsync(PersonId, RoleType.Enrolment, Actor, CanonicalId.NewEventId(), ct);
        var revoked = await service.RevokeAsync(PersonId, RoleType.Enrolment, new RevokeRoleRequest { Version = assigned.Version }, Actor, CanonicalId.NewEventId(), ct);
        var eventsAfterRevoke = sink.Events.Count;

        var secondRevoke = await service.RevokeAsync(PersonId, RoleType.Enrolment, new RevokeRoleRequest { Version = revoked.Version }, Actor, CanonicalId.NewEventId(), ct);

        Assert.Equal(revoked.Version, secondRevoke.Version);
        Assert.Equal(eventsAfterRevoke, sink.Events.Count);
    }

    [Fact]
    public async Task Revoking_a_role_never_assigned_is_404()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = await NewService();

        var error = await Assert.ThrowsAsync<RbacError>(
            () => service.RevokeAsync(PersonId, RoleType.Gate, new RevokeRoleRequest { Version = 1 }, Actor, CanonicalId.NewEventId(), ct));

        Assert.Equal("role_assignment_not_found", error.Code);
    }

    [Fact]
    public async Task Revoking_without_a_version_is_rejected()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = await NewService();
        await service.AssignAsync(PersonId, RoleType.Enrolment, Actor, CanonicalId.NewEventId(), ct);

        var error = await Assert.ThrowsAsync<RbacError>(
            () => service.RevokeAsync(PersonId, RoleType.Enrolment, new RevokeRoleRequest(), Actor, CanonicalId.NewEventId(), ct));

        Assert.Equal("validation_failed", error.Code);
    }

    [Fact]
    public async Task Revoking_with_a_stale_version_is_a_concurrency_conflict()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = await NewService();
        var assigned = await service.AssignAsync(PersonId, RoleType.Enrolment, Actor, CanonicalId.NewEventId(), ct);

        var error = await Assert.ThrowsAsync<RbacError>(() => service.RevokeAsync(
            PersonId, RoleType.Enrolment, new RevokeRoleRequest { Version = assigned.Version + 1 }, Actor, CanonicalId.NewEventId(), ct));

        Assert.Equal("concurrency_conflict", error.Code);
    }

    [Fact]
    public async Task AC02_a_role_assigned_in_tenant_A_is_invisible_when_resolving_tenant_B()
    {
        var ct = TestContext.Current.CancellationToken;
        var repository = new InMemoryPersonRoleAssignmentRepository();
        var serviceA = await NewService(repository, tenantId: TenantId);
        var serviceB = await NewService(repository, tenantId: TenantB);
        await serviceA.AssignAsync(PersonId, RoleType.Enrolment, Actor, CanonicalId.NewEventId(), ct);

        var rolesInB = await serviceB.ListForPersonAsync(PersonId, ct);

        Assert.Empty(rolesInB);
    }

    // --- BIO-IDN-006-QA-01 (Seyi round 1, Critical): only an existing SUPER_ADMIN may mutate SUPER_ADMIN ---

    [Fact]
    public async Task PRIVILEGE_ESCALATION_an_actor_who_does_not_already_hold_SUPER_ADMIN_cannot_assign_it()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = await NewService();
        var unprivilegedActor = new AuditActor(ActorType.User, "unprivileged_attacker");

        var error = await Assert.ThrowsAsync<RbacError>(() => service.AssignAsync(PersonId, RoleType.SuperAdmin, unprivilegedActor, CanonicalId.NewEventId(), ct));

        Assert.Equal("super_admin_mutation_requires_super_admin", error.Code);
    }

    [Fact]
    public async Task PRIVILEGE_ESCALATION_an_actor_who_does_not_already_hold_SUPER_ADMIN_cannot_revoke_it_either()
    {
        // Symmetric with assign - stripping a legitimate admin's access without authority is
        // just as much a security concern as granting unauthorized access (denial-of-access /
        // sabotage vector).
        var ct = TestContext.Current.CancellationToken;
        var repository = new InMemoryPersonRoleAssignmentRepository();
        var bootstrapAdmin = await SeedBootstrapSuperAdminAsync(repository, TenantId, "per_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a01", ct);
        var service = await NewService(repository);
        var assignedToBootstrap = await service.AssignAsync(bootstrapAdmin.Id, RoleType.SuperAdmin, bootstrapAdmin, CanonicalId.NewEventId(), ct);
        var unprivilegedActor = new AuditActor(ActorType.User, "unprivileged_attacker");

        var error = await Assert.ThrowsAsync<RbacError>(() => service.RevokeAsync(
            bootstrapAdmin.Id, RoleType.SuperAdmin, new RevokeRoleRequest { Version = assignedToBootstrap.Version }, unprivilegedActor, CanonicalId.NewEventId(), ct));

        Assert.Equal("super_admin_mutation_requires_super_admin", error.Code);
    }

    [Fact]
    public async Task PRIVILEGE_ESCALATION_a_free_form_non_person_actor_id_can_never_satisfy_the_SUPER_ADMIN_check_even_by_coincidence()
    {
        // The disclosed pre-auth "any asserted actor id" convention this codebase otherwise
        // allows (e.g. "admin_alice") is deliberately never eligible here - the check requires
        // the actor's OWN id to be a well-formed per_ id that genuinely holds an active
        // SUPER_ADMIN record, not merely "any string that isn't obviously wrong".
        var ct = TestContext.Current.CancellationToken;
        var service = await NewService();
        var freeFormActor = new AuditActor(ActorType.User, "admin_alice");

        var error = await Assert.ThrowsAsync<RbacError>(() => service.AssignAsync(PersonId, RoleType.SuperAdmin, freeFormActor, CanonicalId.NewEventId(), ct));

        Assert.Equal("super_admin_mutation_requires_super_admin", error.Code);
    }

    [Fact]
    public async Task A_legitimate_SUPER_ADMIN_can_assign_SUPER_ADMIN_to_someone_else()
    {
        var ct = TestContext.Current.CancellationToken;
        var repository = new InMemoryPersonRoleAssignmentRepository();
        var bootstrapAdmin = await SeedBootstrapSuperAdminAsync(repository, TenantId, "per_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a02", ct);
        var service = await NewService(repository);

        var assignment = await service.AssignAsync(PersonId, RoleType.SuperAdmin, bootstrapAdmin, CanonicalId.NewEventId(), ct);

        Assert.Equal(RoleAssignmentStatus.Active, assignment.Status);
    }

    [Fact]
    public async Task A_legitimate_SUPER_ADMIN_can_revoke_SUPER_ADMIN_from_someone_else()
    {
        var ct = TestContext.Current.CancellationToken;
        var repository = new InMemoryPersonRoleAssignmentRepository();
        var bootstrapAdmin = await SeedBootstrapSuperAdminAsync(repository, TenantId, "per_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a03", ct);
        var service = await NewService(repository);
        var assigned = await service.AssignAsync(PersonId, RoleType.SuperAdmin, bootstrapAdmin, CanonicalId.NewEventId(), ct);

        var revoked = await service.RevokeAsync(PersonId, RoleType.SuperAdmin, new RevokeRoleRequest { Version = assigned.Version }, bootstrapAdmin, CanonicalId.NewEventId(), ct);

        Assert.Equal(RoleAssignmentStatus.Revoked, revoked.Status);
    }

    // --- BIO-IDN-006-QA-03 (Seyi round 2, Critical): every OTHER role also requires real authority ---

    [Fact]
    public async Task PRIVILEGE_ESCALATION_an_unprivileged_actor_cannot_assign_a_non_SUPER_ADMIN_role_either()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = await NewService();
        var unprivilegedActor = new AuditActor(ActorType.User, "unprivileged_attacker");

        var error = await Assert.ThrowsAsync<RbacError>(() => service.AssignAsync(PersonId, RoleType.TenantAdmin, unprivilegedActor, CanonicalId.NewEventId(), ct));

        Assert.Equal("permission_required", error.Code);
    }

    [Fact]
    public async Task PRIVILEGE_ESCALATION_an_unprivileged_actor_cannot_revoke_a_non_SUPER_ADMIN_role_either()
    {
        var ct = TestContext.Current.CancellationToken;
        var repository = new InMemoryPersonRoleAssignmentRepository();
        var service = await NewService(repository);
        var assigned = await service.AssignAsync(PersonId, RoleType.Enrolment, Actor, CanonicalId.NewEventId(), ct);
        var unprivilegedActor = new AuditActor(ActorType.User, "unprivileged_attacker");

        var error = await Assert.ThrowsAsync<RbacError>(() => service.RevokeAsync(
            PersonId, RoleType.Enrolment, new RevokeRoleRequest { Version = assigned.Version }, unprivilegedActor, CanonicalId.NewEventId(), ct));

        Assert.Equal("permission_required", error.Code);
    }

    [Fact]
    public async Task A_role_explicitly_delegated_rbac_role_assign_can_assign_other_roles_without_holding_SUPER_ADMIN()
    {
        var ct = TestContext.Current.CancellationToken;
        var repository = new InMemoryPersonRoleAssignmentRepository();
        var tenantProvider = new FakeCurrentTenantProvider(TenantId);
        var permissions = new PermissionService(
            tenantProvider, new InMemoryRolePermissionGrantRepository(), repository, new InMemoryRbacAuditSink(), new PermissionGrantLockRegistry(), new FakePersonExistenceGateway());
        var service = await NewService(repository, permissionService: permissions);
        const string delegatedPersonId = "per_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a20";
        await service.AssignAsync(delegatedPersonId, RoleType.RegistrarHr, Actor, CanonicalId.NewEventId(), ct); // bootstrap admin grants the delegate its role
        await permissions.SetGrantAsync(
            RoleType.RegistrarHr, RoleAssignmentService.RoleAssignPermission, new SetPermissionGrantRequest { Granted = true }, Actor, CanonicalId.NewEventId(), ct);
        var delegatedActor = new AuditActor(ActorType.User, delegatedPersonId);

        var assignment = await service.AssignAsync(PersonId, RoleType.Enrolment, delegatedActor, CanonicalId.NewEventId(), ct);

        Assert.Equal(RoleAssignmentStatus.Active, assignment.Status);
    }

    // --- BIO-IDN-006-QA-02 (Seyi round 1, High): maker-checker is a REAL, enforced gate on SUPER_ADMIN mutation ---

    [Fact]
    public async Task Maker_checker_genuinely_blocks_a_legitimate_SUPER_ADMIN_from_granting_SUPER_ADMIN_when_the_tenant_requires_second_approval()
    {
        var ct = TestContext.Current.CancellationToken;
        var repository = new InMemoryPersonRoleAssignmentRepository();
        var bootstrapAdmin = await SeedBootstrapSuperAdminAsync(repository, TenantId, "per_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a04", ct);
        var makerChecker = NewMakerCheckerService(TenantId, repository);
        await makerChecker.SetActionRuleAsync(
            "rbac.super_admin_assign", new SetSensitiveActionRuleRequest { RequiresSecondApproval = true }, bootstrapAdmin, CanonicalId.NewEventId(), ct);
        var service = await NewService(repository, makerCheckerService: makerChecker);

        var error = await Assert.ThrowsAsync<RbacError>(() => service.AssignAsync(PersonId, RoleType.SuperAdmin, bootstrapAdmin, CanonicalId.NewEventId(), ct));

        Assert.Equal("second_approval_required", error.Code);
    }

    [Fact]
    public async Task Maker_checker_clears_once_a_second_approval_is_obtained_and_the_mutation_then_succeeds()
    {
        var ct = TestContext.Current.CancellationToken;
        var repository = new InMemoryPersonRoleAssignmentRepository();
        var bootstrapAdmin = await SeedBootstrapSuperAdminAsync(repository, TenantId, "per_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a05", ct);
        var checker = await SeedBootstrapSuperAdminAsync(repository, TenantId, "per_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a06", ct);
        var makerChecker = NewMakerCheckerService(TenantId, repository);
        await makerChecker.SetActionRuleAsync(
            "rbac.super_admin_assign", new SetSensitiveActionRuleRequest { RequiresSecondApproval = true }, bootstrapAdmin, CanonicalId.NewEventId(), ct);
        var request = await makerChecker.RequestApprovalAsync(
            "rbac.super_admin_assign", new RequestApprovalRequest { TargetRef = PersonId, Reason = "onboarding a second platform admin" }, bootstrapAdmin,
            CanonicalId.NewEventId(), ct);
        await makerChecker.DecideAsync(
            "rbac.super_admin_assign", PersonId, request.SequenceNumber, new DecideApprovalRequest { Decision = "APPROVED", ReasonCode = "verified", Version = request.Version },
            checker, CanonicalId.NewEventId(), ct);
        var service = await NewService(repository, makerCheckerService: makerChecker);

        var assignment = await service.AssignAsync(PersonId, RoleType.SuperAdmin, bootstrapAdmin, CanonicalId.NewEventId(), ct);

        Assert.Equal(RoleAssignmentStatus.Active, assignment.Status);
    }

    [Fact]
    public async Task ADVERSARIAL_failed_sensitive_mutation_does_not_consume_its_approval()
    {
        var ct = TestContext.Current.CancellationToken;
        var repository = new InMemoryPersonRoleAssignmentRepository();
        var bootstrapAdmin = await SeedBootstrapSuperAdminAsync(repository, TenantId, BootstrapAdminPersonId, ct);
        const string checkerId = "per_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a06";
        var checker = await SeedBootstrapSuperAdminAsync(repository, TenantId, checkerId, ct);
        var gateway = new FakePersonExistenceGateway(new HashSet<string> { BootstrapAdminPersonId, checkerId });
        var permissions = new PermissionService(
            new FakeCurrentTenantProvider(TenantId), new InMemoryRolePermissionGrantRepository(), repository,
            new InMemoryRbacAuditSink(), new PermissionGrantLockRegistry(), gateway);
        var makerChecker = NewMakerCheckerService(TenantId, repository, permissions);
        await makerChecker.SetActionRuleAsync(
            "rbac.super_admin_assign", new SetSensitiveActionRuleRequest { RequiresSecondApproval = true }, bootstrapAdmin, CanonicalId.NewEventId(), ct);
        var approval = await makerChecker.RequestApprovalAsync(
            "rbac.super_admin_assign", new RequestApprovalRequest { TargetRef = PersonId, Reason = "approved target" }, bootstrapAdmin,
            CanonicalId.NewEventId(), ct);
        await makerChecker.DecideAsync(
            "rbac.super_admin_assign", PersonId, approval.SequenceNumber,
            new DecideApprovalRequest { Decision = "APPROVED", ReasonCode = "verified", Version = approval.Version },
            checker, CanonicalId.NewEventId(), ct);
        var service = await NewService(repository, personGateway: gateway, makerCheckerService: makerChecker, permissionService: permissions, seedBootstrapAdmin: false);

        var error = await Assert.ThrowsAsync<RbacError>(() =>
            service.AssignAsync(PersonId, RoleType.SuperAdmin, bootstrapAdmin, CanonicalId.NewEventId(), ct));
        var history = await makerChecker.ListHistoryAsync("rbac.super_admin_assign", PersonId, ct);

        Assert.Equal("person_not_found", error.Code);
        Assert.Null(history.Single().ConsumedAt);
    }

    [Fact]
    public async Task BIOIDN006_QA06_a_failed_mutations_untouched_approval_genuinely_works_on_retry()
    {
        // The direct, practical consequence of QA-06's fix: a maker/checker pair never has to
        // raise and re-approve a whole new request just because the FIRST attempt happened to
        // hit a transient/retryable failure - the SAME approval clears the retried mutation.
        var ct = TestContext.Current.CancellationToken;
        var repository = new InMemoryPersonRoleAssignmentRepository();
        var bootstrapAdmin = await SeedBootstrapSuperAdminAsync(repository, TenantId, BootstrapAdminPersonId, ct);
        const string checkerId = "per_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a23";
        var checker = await SeedBootstrapSuperAdminAsync(repository, TenantId, checkerId, ct);
        var knownPersonIds = new HashSet<string> { BootstrapAdminPersonId, checkerId }; // PersonId NOT yet present
        var gateway = new FakePersonExistenceGateway(knownPersonIds);
        var permissions = new PermissionService(
            new FakeCurrentTenantProvider(TenantId), new InMemoryRolePermissionGrantRepository(), repository,
            new InMemoryRbacAuditSink(), new PermissionGrantLockRegistry(), gateway);
        var makerChecker = NewMakerCheckerService(TenantId, repository, permissions);
        await makerChecker.SetActionRuleAsync(
            "rbac.super_admin_assign", new SetSensitiveActionRuleRequest { RequiresSecondApproval = true }, bootstrapAdmin, CanonicalId.NewEventId(), ct);
        var approval = await makerChecker.RequestApprovalAsync(
            "rbac.super_admin_assign", new RequestApprovalRequest { TargetRef = PersonId, Reason = "approved target" }, bootstrapAdmin, CanonicalId.NewEventId(), ct);
        await makerChecker.DecideAsync(
            "rbac.super_admin_assign", PersonId, approval.SequenceNumber,
            new DecideApprovalRequest { Decision = "APPROVED", ReasonCode = "verified", Version = approval.Version }, checker, CanonicalId.NewEventId(), ct);
        var service = await NewService(repository, personGateway: gateway, makerCheckerService: makerChecker, permissionService: permissions, seedBootstrapAdmin: false);
        var firstAttempt = await Assert.ThrowsAsync<RbacError>(() => service.AssignAsync(PersonId, RoleType.SuperAdmin, bootstrapAdmin, CanonicalId.NewEventId(), ct));
        Assert.Equal("person_not_found", firstAttempt.Code); // the retryable failure

        knownPersonIds.Add(PersonId); // the transient condition clears (e.g. the person registers)
        var assignment = await service.AssignAsync(PersonId, RoleType.SuperAdmin, bootstrapAdmin, CanonicalId.NewEventId(), ct);

        Assert.Equal(RoleAssignmentStatus.Active, assignment.Status); // the SAME approval cleared the retry
    }

    [Fact]
    public async Task BIOIDN006_QA06_an_idempotent_no_op_mutation_never_consumes_the_approval()
    {
        var ct = TestContext.Current.CancellationToken;
        var repository = new InMemoryPersonRoleAssignmentRepository();
        var bootstrapAdmin = await SeedBootstrapSuperAdminAsync(repository, TenantId, BootstrapAdminPersonId, ct);
        await SeedBootstrapSuperAdminAsync(repository, TenantId, PersonId, ct); // already ACTIVE - the second assign is a no-op
        var makerChecker = NewMakerCheckerService(TenantId, repository);
        await makerChecker.SetActionRuleAsync(
            "rbac.super_admin_assign", new SetSensitiveActionRuleRequest { RequiresSecondApproval = true }, bootstrapAdmin, CanonicalId.NewEventId(), ct);
        var approval = await makerChecker.RequestApprovalAsync(
            "rbac.super_admin_assign", new RequestApprovalRequest { TargetRef = PersonId, Reason = "approved target" }, bootstrapAdmin, CanonicalId.NewEventId(), ct);
        var checker = await SeedBootstrapSuperAdminAsync(repository, TenantId, "per_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a24", ct);
        await makerChecker.DecideAsync(
            "rbac.super_admin_assign", PersonId, approval.SequenceNumber,
            new DecideApprovalRequest { Decision = "APPROVED", ReasonCode = "verified", Version = approval.Version }, checker, CanonicalId.NewEventId(), ct);
        var service = await NewService(repository, makerCheckerService: makerChecker);

        var noOp = await service.AssignAsync(PersonId, RoleType.SuperAdmin, bootstrapAdmin, CanonicalId.NewEventId(), ct);
        var history = await makerChecker.ListHistoryAsync("rbac.super_admin_assign", PersonId, ct);

        Assert.Equal(RoleAssignmentStatus.Active, noOp.Status);
        Assert.Null(history.Single().ConsumedAt); // the no-op never touched the approval
    }

    [Fact]
    public async Task Maker_checker_authorization_gate_runs_before_the_SUPER_ADMIN_authority_gate_never_leaks_which_failed_first_to_an_unprivileged_caller()
    {
        // An unauthorized actor's request is rejected for lack of SUPER_ADMIN authority even
        // when a maker-checker rule is ALSO configured and would separately block it - the
        // authority gate runs first, so an unprivileged caller never learns anything about
        // whether maker-checker is even configured for this action.
        var ct = TestContext.Current.CancellationToken;
        var repository = new InMemoryPersonRoleAssignmentRepository();
        var bootstrapAdmin = await SeedBootstrapSuperAdminAsync(repository, TenantId, "per_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a07", ct);
        var makerChecker = NewMakerCheckerService(TenantId, repository);
        await makerChecker.SetActionRuleAsync(
            "rbac.super_admin_assign", new SetSensitiveActionRuleRequest { RequiresSecondApproval = true }, bootstrapAdmin, CanonicalId.NewEventId(), ct);
        var service = await NewService(repository, makerCheckerService: makerChecker);
        var unprivilegedActor = new AuditActor(ActorType.User, "unprivileged_attacker");

        var error = await Assert.ThrowsAsync<RbacError>(() => service.AssignAsync(PersonId, RoleType.SuperAdmin, unprivilegedActor, CanonicalId.NewEventId(), ct));

        Assert.Equal("super_admin_mutation_requires_super_admin", error.Code); // not second_approval_required
    }

    // --- Applying BIO-IDN-002-QA-01 from the start ---

    [Fact]
    public async Task Assigning_a_role_for_a_person_that_does_not_exist_in_the_resolved_tenant_is_rejected()
    {
        var ct = TestContext.Current.CancellationToken;
        // The gateway must still resolve the ACTOR (the authority gate runs first) - only the
        // TARGET person is absent, so this genuinely reaches and exercises the existence check.
        var service = await NewService(personGateway: new FakePersonExistenceGateway(new HashSet<string> { BootstrapAdminPersonId }));

        var error = await Assert.ThrowsAsync<RbacError>(() => service.AssignAsync(PersonId, RoleType.Enrolment, Actor, CanonicalId.NewEventId(), ct));

        Assert.Equal("person_not_found", error.Code);
    }

    [Fact]
    public async Task The_existence_check_fails_closed_no_audit_event_and_nothing_is_stored()
    {
        var ct = TestContext.Current.CancellationToken;
        var sink = new InMemoryRbacAuditSink();
        var repository = new InMemoryPersonRoleAssignmentRepository();
        var service = await NewService(repository, sink, personGateway: new FakePersonExistenceGateway(new HashSet<string>()));

        await Assert.ThrowsAsync<RbacError>(() => service.AssignAsync(PersonId, RoleType.Enrolment, Actor, CanonicalId.NewEventId(), ct));

        Assert.Empty(sink.Events);
        Assert.Empty(await repository.ListForPersonAsync(TenantId, PersonId, ct));
    }

    // --- ADR-0003 Decision 7: audit-atomicity ---

    [Fact]
    public async Task Assign_records_exactly_one_allowed_audit_event()
    {
        var ct = TestContext.Current.CancellationToken;
        var sink = new InMemoryRbacAuditSink();
        var service = await NewService(auditSink: sink);

        await service.AssignAsync(PersonId, RoleType.Enrolment, Actor, CanonicalId.NewEventId(), ct);

        var evt = Assert.Single(sink.Events);
        Assert.Equal("rbac.role_assign", evt.Action);
        Assert.Equal(AuditOutcome.Allowed, evt.Outcome);
        Assert.Equal(TenantId, evt.TenantId);
    }

    [Fact]
    public async Task Revoke_records_an_audit_event()
    {
        var ct = TestContext.Current.CancellationToken;
        var sink = new InMemoryRbacAuditSink();
        var service = await NewService(auditSink: sink);
        var assigned = await service.AssignAsync(PersonId, RoleType.Enrolment, Actor, CanonicalId.NewEventId(), ct);

        await service.RevokeAsync(PersonId, RoleType.Enrolment, new RevokeRoleRequest { Version = assigned.Version }, Actor, CanonicalId.NewEventId(), ct);

        Assert.Contains(sink.Events, e => e.Action == "rbac.role_revoke");
    }

    [Fact]
    public async Task Assign_is_never_committed_when_the_audit_sink_fails()
    {
        var ct = TestContext.Current.CancellationToken;
        var repository = new InMemoryPersonRoleAssignmentRepository();
        var service = await NewService(repository, new ThrowingAuditSink());

        var error = await Assert.ThrowsAsync<RbacError>(() => service.AssignAsync(PersonId, RoleType.Enrolment, Actor, CanonicalId.NewEventId(), ct));

        Assert.Equal("audit_unavailable", error.Code);
        Assert.Empty(await repository.ListForPersonAsync(TenantId, PersonId, ct));
    }

    [Fact]
    public async Task Revoke_is_never_committed_when_the_audit_sink_fails()
    {
        var ct = TestContext.Current.CancellationToken;
        var repository = new InMemoryPersonRoleAssignmentRepository();
        var assigned = await (await NewService(repository)).AssignAsync(PersonId, RoleType.Enrolment, Actor, CanonicalId.NewEventId(), ct);
        var service = await NewService(repository, new ThrowingAuditSink());

        var error = await Assert.ThrowsAsync<RbacError>(() => service.RevokeAsync(
            PersonId, RoleType.Enrolment, new RevokeRoleRequest { Version = assigned.Version }, Actor, CanonicalId.NewEventId(), ct));

        Assert.Equal("audit_unavailable", error.Code);
        var stillStored = await repository.GetAsync(TenantId, PersonId, RoleType.Enrolment, ct);
        Assert.Equal(RoleAssignmentStatus.Active, stillStored!.Status);
    }

    [Fact]
    public async Task Two_genuinely_concurrent_revokes_of_the_same_assignment_never_produce_a_false_allowed_audit()
    {
        var ct = TestContext.Current.CancellationToken;
        var repository = new InMemoryPersonRoleAssignmentRepository();
        await SeedBootstrapSuperAdminAsync(repository, TenantId, BootstrapAdminPersonId, ct);
        var permissions = new PermissionService(
            new FakeCurrentTenantProvider(TenantId), new InMemoryRolePermissionGrantRepository(), repository, new InMemoryRbacAuditSink(),
            new PermissionGrantLockRegistry(), new FakePersonExistenceGateway());
        var plainSink = new InMemoryRbacAuditSink();
        var lockRegistry = new RoleAssignmentLockRegistry(); // shared: both callers must serialize
        var assigned = await new RoleAssignmentService(
                new FakeCurrentTenantProvider(TenantId), repository, plainSink, lockRegistry, new FakePersonExistenceGateway(),
                NewMakerCheckerService(TenantId, repository, permissions), permissions)
            .AssignAsync(PersonId, RoleType.Enrolment, Actor, CanonicalId.NewEventId(), ct);

        var delayingSink = new DelayFirstCallAuditSink(plainSink);
        var service = new RoleAssignmentService(
            new FakeCurrentTenantProvider(TenantId), repository, delayingSink, lockRegistry, new FakePersonExistenceGateway(),
            NewMakerCheckerService(TenantId, repository, permissions), permissions);

        var taskA = service.RevokeAsync(PersonId, RoleType.Enrolment, new RevokeRoleRequest { Version = assigned.Version }, Actor, CanonicalId.NewEventId(), ct);
        var taskB = service.RevokeAsync(PersonId, RoleType.Enrolment, new RevokeRoleRequest { Version = assigned.Version }, Actor, CanonicalId.NewEventId(), ct);
        var results = await Task.WhenAll(taskA, taskB);

        // The lock fully serializes: the winner performs the real revoke; the loser re-reads
        // under the lock, sees Revoked already, and hits the no-op branch BEFORE the version
        // check even runs - RevokeAsync's own idempotent "ensure revoked" semantics mean the
        // loser never throws at all here, it just observes the already-achieved target state.
        Assert.All(results, r => Assert.Equal(RoleAssignmentStatus.Revoked, r.Status));
        var revokeEvents = plainSink.Events.Where(e => e.Action == "rbac.role_revoke").ToList();
        Assert.Single(revokeEvents); // exactly one caller's write was ever audited, not two
    }

    private sealed class ThrowingAuditSink : IRbacAuditSink
    {
        public Task WriteAsync(RbacAuditEvent auditEvent, CancellationToken cancellationToken = default) => throw new InvalidOperationException("synthetic sink outage");

        public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("synthetic sink outage: cannot even verify");
    }

    private sealed class DelayFirstCallAuditSink(IRbacAuditSink inner) : IRbacAuditSink
    {
        private int _calls;

        public async Task WriteAsync(RbacAuditEvent auditEvent, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _calls) == 1)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
            }

            await inner.WriteAsync(auditEvent, cancellationToken);
        }

        public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default) => inner.ExistsAsync(eventId, cancellationToken);
    }
}

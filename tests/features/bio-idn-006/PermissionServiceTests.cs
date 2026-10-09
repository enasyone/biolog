using Biolog.Features.Rbac.Api;
using Biolog.Features.Rbac.Audit;
using Biolog.Features.Rbac.Domain;
using Biolog.Features.Rbac.Identifiers;
using Xunit;

namespace Biolog.Features.Rbac.Tests;

/// <summary>BIO-IDN-006-AC02: tenant-scoped permission grants, and the real HasPermissionAsync verification surface BIO-IDN-004's own PermissionResolver documented itself as waiting for.</summary>
public sealed class PermissionServiceTests
{
    private const string BootstrapAdminPersonId = "per_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a97";
    private static readonly AuditActor Actor = new(ActorType.User, BootstrapAdminPersonId);
    private const string TenantId = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77";
    private const string TenantB = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a88";
    private const string PersonId = "per_0199e0c1-9d40-7e52-b103-7c9a4e2f6b1d";
    private const string PermissionKey = "person.reactivate";

    private sealed record Harness(PermissionService Permissions, RoleAssignmentService Roles, IRolePermissionGrantRepository GrantRepository, InMemoryRbacAuditSink AuditSink, IPersonRoleAssignmentRepository AssignmentRepository);

    private static async Task<Harness> NewHarness(string tenantId = TenantId, IPersonRoleAssignmentRepository? assignmentRepository = null, InMemoryRbacAuditSink? auditSink = null, CancellationToken cancellationToken = default)
    {
        var assignments = assignmentRepository ?? new InMemoryPersonRoleAssignmentRepository();
        var grants = new InMemoryRolePermissionGrantRepository();
        var sink = auditSink ?? new InMemoryRbacAuditSink();
        var tenantProvider = new FakeCurrentTenantProvider(tenantId);
        var gateway = new FakePersonExistenceGateway();

        if (await assignments.GetAsync(tenantId, BootstrapAdminPersonId, RoleType.SuperAdmin, cancellationToken) is null)
        {
            await RoleAssignmentServiceTests.SeedBootstrapSuperAdminAsync(assignments, tenantId, BootstrapAdminPersonId, cancellationToken);
        }

        var permissions = new PermissionService(tenantProvider, grants, assignments, sink, new PermissionGrantLockRegistry(), gateway);
        var makerChecker = RoleAssignmentServiceTests.NewMakerCheckerService(tenantId, assignments, permissions);
        var roles = new RoleAssignmentService(
            tenantProvider, assignments, sink, new RoleAssignmentLockRegistry(), gateway, makerChecker, permissions);
        return new Harness(permissions, roles, grants, sink, assignments);
    }

    [Fact]
    public async Task AC02_a_person_with_no_roles_has_no_permissions()
    {
        var ct = TestContext.Current.CancellationToken;
        var h = await NewHarness();

        var (granted, viaRoles) = await h.Permissions.HasPermissionAsync(PersonId, PermissionKey, ct);

        Assert.False(granted);
        Assert.Empty(viaRoles);
    }

    [Fact]
    public async Task Fail_closed_default_a_role_with_no_explicit_grant_carries_no_permission()
    {
        var ct = TestContext.Current.CancellationToken;
        var h = await NewHarness();
        await h.Roles.AssignAsync(PersonId, RoleType.Enrolment, Actor, CanonicalId.NewEventId(), ct);

        var (granted, _) = await h.Permissions.HasPermissionAsync(PersonId, PermissionKey, ct);

        Assert.False(granted);
    }

    [Fact]
    public async Task Granting_a_permission_to_a_role_the_person_holds_makes_HasPermission_true()
    {
        var ct = TestContext.Current.CancellationToken;
        var h = await NewHarness();
        await h.Roles.AssignAsync(PersonId, RoleType.Enrolment, Actor, CanonicalId.NewEventId(), ct);
        await h.Permissions.SetGrantAsync(RoleType.Enrolment, PermissionKey, new SetPermissionGrantRequest { Granted = true }, Actor, CanonicalId.NewEventId(), ct);

        var (granted, viaRoles) = await h.Permissions.HasPermissionAsync(PersonId, PermissionKey, ct);

        Assert.True(granted);
        Assert.Contains(RoleType.Enrolment, viaRoles);
    }

    [Fact]
    public async Task Revoking_the_underlying_role_immediately_removes_the_permission_no_stale_bypass()
    {
        var ct = TestContext.Current.CancellationToken;
        var h = await NewHarness();
        var assigned = await h.Roles.AssignAsync(PersonId, RoleType.Enrolment, Actor, CanonicalId.NewEventId(), ct);
        await h.Permissions.SetGrantAsync(RoleType.Enrolment, PermissionKey, new SetPermissionGrantRequest { Granted = true }, Actor, CanonicalId.NewEventId(), ct);
        Assert.True((await h.Permissions.HasPermissionAsync(PersonId, PermissionKey, ct)).Granted);

        await h.Roles.RevokeAsync(PersonId, RoleType.Enrolment, new RevokeRoleRequest { Version = assigned.Version }, Actor, CanonicalId.NewEventId(), ct);

        var (granted, _) = await h.Permissions.HasPermissionAsync(PersonId, PermissionKey, ct);
        Assert.False(granted); // live check, never cached
    }

    [Fact]
    public async Task Setting_granted_to_false_after_true_revokes_the_permission()
    {
        var ct = TestContext.Current.CancellationToken;
        var h = await NewHarness();
        await h.Roles.AssignAsync(PersonId, RoleType.Enrolment, Actor, CanonicalId.NewEventId(), ct);
        await h.Permissions.SetGrantAsync(RoleType.Enrolment, PermissionKey, new SetPermissionGrantRequest { Granted = true }, Actor, CanonicalId.NewEventId(), ct);

        await h.Permissions.SetGrantAsync(RoleType.Enrolment, PermissionKey, new SetPermissionGrantRequest { Granted = false }, Actor, CanonicalId.NewEventId(), ct);

        Assert.False((await h.Permissions.HasPermissionAsync(PersonId, PermissionKey, ct)).Granted);
    }

    // --- SUPER_ADMIN: implicit, non-configurable ---

    [Fact]
    public async Task SUPER_ADMIN_holds_every_permission_with_no_grant_ever_configured()
    {
        var ct = TestContext.Current.CancellationToken;
        var h = await NewHarness();
        // Seeded directly via the repository - the same out-of-band bootstrap path production
        // tenant provisioning would use (BIO-IDN-006-QA-01: no actor may self-assign SUPER_ADMIN
        // through the service - see RoleAssignmentService's own header).
        await RoleAssignmentServiceTests.SeedBootstrapSuperAdminAsync(h.AssignmentRepository, TenantId, PersonId, ct);

        var (granted, viaRoles) = await h.Permissions.HasPermissionAsync(PersonId, "anything.at_all", ct);

        Assert.True(granted);
        Assert.Contains(RoleType.SuperAdmin, viaRoles);
    }

    [Fact]
    public async Task PRIVILEGE_ESCALATION_a_grant_can_never_be_configured_for_SUPER_ADMIN()
    {
        // A tenant admin misconfiguring (or an attacker exploiting) a grant record for
        // SUPER_ADMIN could otherwise NARROW its authority to exactly the granted set - this
        // must be structurally impossible, not merely undocumented.
        var ct = TestContext.Current.CancellationToken;
        var h = await NewHarness();

        var error = await Assert.ThrowsAsync<RbacError>(() => h.Permissions.SetGrantAsync(
            RoleType.SuperAdmin, PermissionKey, new SetPermissionGrantRequest { Granted = false }, Actor, CanonicalId.NewEventId(), ct));

        Assert.Equal("super_admin_grant_not_configurable", error.Code);
    }

    [Fact]
    public async Task PRIVILEGE_ESCALATION_holding_an_unrelated_role_with_its_own_grant_never_implies_SUPER_ADMIN_authority()
    {
        var ct = TestContext.Current.CancellationToken;
        var h = await NewHarness();
        await h.Roles.AssignAsync(PersonId, RoleType.Enrolment, Actor, CanonicalId.NewEventId(), ct);
        await h.Permissions.SetGrantAsync(RoleType.Enrolment, "enrolment.register", new SetPermissionGrantRequest { Granted = true }, Actor, CanonicalId.NewEventId(), ct);

        var (granted, _) = await h.Permissions.HasPermissionAsync(PersonId, "tenant.delete_all_data", ct);

        Assert.False(granted); // an unrelated grant never cascades into a different permission
    }

    // --- BIO-IDN-006-QA-03 (Seyi round 2, Critical): configuring ANY role's grants also requires real authority ---

    [Fact]
    public async Task PRIVILEGE_ESCALATION_an_unprivileged_actor_cannot_configure_a_permission_grant_for_any_role()
    {
        var ct = TestContext.Current.CancellationToken;
        var h = await NewHarness();
        var unprivilegedActor = new AuditActor(ActorType.User, "unprivileged_attacker");

        var error = await Assert.ThrowsAsync<RbacError>(() => h.Permissions.SetGrantAsync(
            RoleType.Enrolment, PermissionKey, new SetPermissionGrantRequest { Granted = true }, unprivilegedActor, CanonicalId.NewEventId(), ct));

        Assert.Equal("permission_required", error.Code);
    }

    [Fact]
    public async Task A_role_explicitly_delegated_rbac_permission_grant_manage_can_configure_grants_without_holding_SUPER_ADMIN()
    {
        var ct = TestContext.Current.CancellationToken;
        var h = await NewHarness();
        const string delegatedPersonId = "per_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a21";
        await h.Roles.AssignAsync(delegatedPersonId, RoleType.TenantAdmin, Actor, CanonicalId.NewEventId(), ct);
        await h.Permissions.SetGrantAsync(
            RoleType.TenantAdmin, PermissionService.PermissionGrantManagePermission, new SetPermissionGrantRequest { Granted = true }, Actor, CanonicalId.NewEventId(), ct);
        var delegatedActor = new AuditActor(ActorType.User, delegatedPersonId);

        var grant = await h.Permissions.SetGrantAsync(
            RoleType.Enrolment, PermissionKey, new SetPermissionGrantRequest { Granted = true }, delegatedActor, CanonicalId.NewEventId(), ct);

        Assert.True(grant.Granted);
    }

    [Fact]
    public async Task Rejects_a_malformed_permission_key()
    {
        var ct = TestContext.Current.CancellationToken;
        var h = await NewHarness();

        var error = await Assert.ThrowsAsync<RbacError>(() => h.Permissions.SetGrantAsync(
            RoleType.Enrolment, "not a namespaced key", new SetPermissionGrantRequest { Granted = true }, Actor, CanonicalId.NewEventId(), ct));

        Assert.Equal("validation_failed", error.Code);
    }

    [Fact]
    public async Task AC02_a_grant_configured_in_tenant_A_never_applies_in_tenant_B()
    {
        var ct = TestContext.Current.CancellationToken;
        var assignments = new InMemoryPersonRoleAssignmentRepository();
        var hA = await NewHarness(TenantId, assignments);
        var hB = await NewHarness(TenantB, assignments);
        await hA.Roles.AssignAsync(PersonId, RoleType.Enrolment, Actor, CanonicalId.NewEventId(), ct);
        await hA.Permissions.SetGrantAsync(RoleType.Enrolment, PermissionKey, new SetPermissionGrantRequest { Granted = true }, Actor, CanonicalId.NewEventId(), ct);
        await hB.Roles.AssignAsync(PersonId, RoleType.Enrolment, Actor, CanonicalId.NewEventId(), ct); // same role, different tenant

        var (grantedInB, _) = await hB.Permissions.HasPermissionAsync(PersonId, PermissionKey, ct);

        Assert.False(grantedInB); // the grant lives only in tenant A
    }

    [Fact]
    public async Task HasPermission_for_an_unknown_person_is_404()
    {
        var ct = TestContext.Current.CancellationToken;
        var h = new PermissionService(
            new FakeCurrentTenantProvider(TenantId), new InMemoryRolePermissionGrantRepository(), new InMemoryPersonRoleAssignmentRepository(),
            new InMemoryRbacAuditSink(), new PermissionGrantLockRegistry(), new FakePersonExistenceGateway(new HashSet<string>()));

        var error = await Assert.ThrowsAsync<RbacError>(() => h.HasPermissionAsync(PersonId, PermissionKey, ct));

        Assert.Equal("person_not_found", error.Code);
    }

    [Fact]
    public async Task SetGrant_records_an_audit_event()
    {
        var ct = TestContext.Current.CancellationToken;
        var h = await NewHarness();

        await h.Permissions.SetGrantAsync(RoleType.Enrolment, PermissionKey, new SetPermissionGrantRequest { Granted = true }, Actor, CanonicalId.NewEventId(), ct);

        Assert.Contains(h.AuditSink.Events, e => e.Action == "rbac.permission_grant_set");
    }
}

using System.Reflection;
using Biolog.Features.Rbac.Domain;
using Biolog.Features.Rbac.Identifiers;
using Xunit;

namespace Biolog.Features.Rbac.Tests;

/// <summary>
/// Applies BIO-IDN-001-QA-01's lesson from the start, rather than waiting for a review round to
/// find the same class of gap here too: every persistence boundary in this project must refuse
/// an identity change and an illegal initial state, and every aggregate/repository/service must
/// be inaccessible to ordinary production consumers. This test project has
/// <c>InternalsVisibleTo</c> access specifically so these attacks can be driven directly.
/// </summary>
public sealed class RbacGovernanceTests
{
    private const string TenantA = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77";
    private const string TenantB = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a88";
    private const string PersonId = "per_0199e0c1-9d40-7e52-b103-7c9a4e2f6b1d";

    // --- PersonRoleAssignment ---

    private static PersonRoleAssignment NewAssignment(string tenantId) =>
        new(tenantId, PersonId, RoleType.Enrolment) { Status = RoleAssignmentStatus.Active, Version = 1, CreatedAt = UtcInstant.Now(), UpdatedAt = UtcInstant.Now() };

    [Fact]
    public async Task RoleAssignment_CreateAsync_rejects_an_initial_state_that_is_not_ACTIVE_version_1()
    {
        var ct = TestContext.Current.CancellationToken;
        var repository = new InMemoryPersonRoleAssignmentRepository();
        var notInitial = NewAssignment(TenantA) with { Status = RoleAssignmentStatus.Revoked };

        var error = await Assert.ThrowsAsync<RbacError>(() => repository.CreateAsync(notInitial, ct));

        Assert.Equal("invalid_initial_state", error.Code);
    }

    [Fact]
    public async Task RoleAssignment_ReplaceAsync_rejects_moving_to_a_foreign_tenant()
    {
        var ct = TestContext.Current.CancellationToken;
        var repository = new InMemoryPersonRoleAssignmentRepository();
        var original = await repository.CreateAsync(NewAssignment(TenantA), ct);
        var forged = new PersonRoleAssignment(TenantB, PersonId, RoleType.Enrolment)
        { Status = RoleAssignmentStatus.Revoked, Version = 2, CreatedAt = original.CreatedAt, UpdatedAt = UtcInstant.Now() };

        var error = await Assert.ThrowsAsync<RbacError>(() => repository.ReplaceAsync(original, forged, ct));

        Assert.Equal("identity_immutable", error.Code);
        Assert.NotNull(await repository.GetAsync(TenantA, PersonId, RoleType.Enrolment, ct));
        Assert.Null(await repository.GetAsync(TenantB, PersonId, RoleType.Enrolment, ct));
    }

    [Fact]
    public async Task RoleAssignment_ReplaceAsync_rejects_a_stale_expected_version()
    {
        var ct = TestContext.Current.CancellationToken;
        var repository = new InMemoryPersonRoleAssignmentRepository();
        var original = await repository.CreateAsync(NewAssignment(TenantA), ct);
        await repository.ReplaceAsync(original, original with { Version = 2 }, ct);

        var error = await Assert.ThrowsAsync<RbacError>(() => repository.ReplaceAsync(original, original with { Version = 2 }, ct));

        Assert.Equal("concurrency_conflict", error.Code);
    }

    [Fact]
    public void IPersonRoleAssignmentRepository_and_its_implementation_are_not_public()
    {
        Assert.False(typeof(IPersonRoleAssignmentRepository).IsPublic);
        Assert.False(typeof(InMemoryPersonRoleAssignmentRepository).IsPublic);
    }

    [Fact]
    public void PersonRoleAssignment_has_no_publicly_accessible_constructor()
    {
        Assert.Empty(typeof(PersonRoleAssignment).GetConstructors(BindingFlags.Public | BindingFlags.Instance));
    }

    // --- RolePermissionGrant ---

    [Fact]
    public void IRolePermissionGrantRepository_and_its_implementation_are_not_public()
    {
        Assert.False(typeof(IRolePermissionGrantRepository).IsPublic);
        Assert.False(typeof(InMemoryRolePermissionGrantRepository).IsPublic);
    }

    [Fact]
    public void RolePermissionGrant_has_no_publicly_accessible_constructor()
    {
        Assert.Empty(typeof(RolePermissionGrant).GetConstructors(BindingFlags.Public | BindingFlags.Instance));
    }

    // --- SensitiveActionRule ---

    [Fact]
    public void ISensitiveActionRuleRepository_and_its_implementation_are_not_public()
    {
        Assert.False(typeof(ISensitiveActionRuleRepository).IsPublic);
        Assert.False(typeof(InMemorySensitiveActionRuleRepository).IsPublic);
    }

    [Fact]
    public void SensitiveActionRule_has_no_publicly_accessible_constructor()
    {
        Assert.Empty(typeof(SensitiveActionRule).GetConstructors(BindingFlags.Public | BindingFlags.Instance));
    }

    // --- ApprovalRequest ---

    private static ApprovalRequest NewRequest(string tenantId, string actionKey = "person.archive", string targetRef = PersonId, int sequenceNumber = 1) =>
        new(tenantId, actionKey, targetRef, sequenceNumber)
        { Status = ApprovalRequestStatus.Pending, RequestedByActorId = "maker_1", RequestReason = "x", Version = 1, CreatedAt = UtcInstant.Now(), UpdatedAt = UtcInstant.Now() };

    [Fact]
    public async Task ApprovalRequest_CreateAsync_rejects_an_initial_state_that_is_not_PENDING_version_1()
    {
        var ct = TestContext.Current.CancellationToken;
        var repository = new InMemoryApprovalRequestRepository();
        var notInitial = NewRequest(TenantA) with { Status = ApprovalRequestStatus.Approved };

        var error = await Assert.ThrowsAsync<RbacError>(() => repository.CreateAsync(notInitial, ct));

        Assert.Equal("invalid_initial_state", error.Code);
    }

    [Fact]
    public async Task ApprovalRequest_CreateAsync_rejects_a_second_PENDING_request_for_the_same_target()
    {
        var ct = TestContext.Current.CancellationToken;
        var repository = new InMemoryApprovalRequestRepository();
        await repository.CreateAsync(NewRequest(TenantA), ct);
        var second = NewRequest(TenantA, sequenceNumber: 2);

        var error = await Assert.ThrowsAsync<RbacError>(() => repository.CreateAsync(second, ct));

        Assert.Equal("approval_request_already_pending", error.Code);
    }

    [Fact]
    public async Task ApprovalRequest_ReplaceAsync_rejects_moving_to_a_foreign_tenant()
    {
        var ct = TestContext.Current.CancellationToken;
        var repository = new InMemoryApprovalRequestRepository();
        var original = await repository.CreateAsync(NewRequest(TenantA), ct);
        var forged = new ApprovalRequest(TenantB, original.ActionKey, original.TargetRef, original.SequenceNumber)
        {
            Status = ApprovalRequestStatus.Approved, RequestedByActorId = original.RequestedByActorId, RequestReason = original.RequestReason,
            DecidedByActorId = "checker_1", DecidedAt = UtcInstant.Now(), Version = 2, CreatedAt = original.CreatedAt, UpdatedAt = UtcInstant.Now(),
        };

        var error = await Assert.ThrowsAsync<RbacError>(() => repository.ReplaceAsync(original, forged, "checker_1", ct));

        Assert.Equal("identity_immutable", error.Code);
    }

    [Fact]
    public async Task ApprovalRequest_ReplaceAsync_rejects_replacing_an_already_decided_request_a_second_time()
    {
        var ct = TestContext.Current.CancellationToken;
        var repository = new InMemoryApprovalRequestRepository();
        var original = await repository.CreateAsync(NewRequest(TenantA), ct);
        var decided = await repository.ReplaceAsync(
            original, original with { Status = ApprovalRequestStatus.Approved, DecidedByActorId = "checker_1", Version = 2 }, "checker_1", ct);
        var secondAttempt = decided with { Status = ApprovalRequestStatus.Rejected, Version = 3 };

        var error = await Assert.ThrowsAsync<RbacError>(() => repository.ReplaceAsync(decided, secondAttempt, "checker_2", ct));

        Assert.Equal("approval_request_already_decided", error.Code);
    }

    [Fact]
    public async Task ApprovalRequest_ReplaceAsync_rejects_a_stale_expected_version()
    {
        var ct = TestContext.Current.CancellationToken;
        var repository = new InMemoryApprovalRequestRepository();
        var original = await repository.CreateAsync(NewRequest(TenantA), ct);
        await repository.ReplaceAsync(original, original with { Status = ApprovalRequestStatus.Approved, DecidedByActorId = "checker_1", Version = 2 }, "checker_1", ct);

        var error = await Assert.ThrowsAsync<RbacError>(() => repository.ReplaceAsync(
            original, original with { Status = ApprovalRequestStatus.Rejected, DecidedByActorId = "checker_2", Version = 2 }, "checker_2", ct));

        Assert.Equal("concurrency_conflict", error.Code);
    }

    [Fact]
    public async Task ApprovalRequest_ConsumeAsync_rejects_a_request_that_is_not_yet_APPROVED()
    {
        // BIO-IDN-006-QA-05's own repository-boundary re-check, independent of
        // MakerCheckerService.TryConsumeApprovalAsync's pre-lock check.
        var ct = TestContext.Current.CancellationToken;
        var repository = new InMemoryApprovalRequestRepository();
        var pending = await repository.CreateAsync(NewRequest(TenantA), ct);

        var error = await Assert.ThrowsAsync<RbacError>(() => repository.ConsumeAsync(pending, pending with { ConsumedAt = UtcInstant.Now(), Version = 2 }, ct));

        Assert.Equal("approval_not_approved", error.Code);
    }

    [Fact]
    public async Task ApprovalRequest_ConsumeAsync_rejects_a_request_that_was_already_consumed()
    {
        var ct = TestContext.Current.CancellationToken;
        var repository = new InMemoryApprovalRequestRepository();
        var original = await repository.CreateAsync(NewRequest(TenantA), ct);
        var approved = await repository.ReplaceAsync(
            original, original with { Status = ApprovalRequestStatus.Approved, DecidedByActorId = "checker_1", Version = 2 }, "checker_1", ct);
        var consumed = await repository.ConsumeAsync(approved, approved with { ConsumedAt = UtcInstant.Now(), Version = 3 }, ct);

        var error = await Assert.ThrowsAsync<RbacError>(() => repository.ConsumeAsync(consumed, consumed with { Version = 4 }, ct));

        Assert.Equal("approval_already_consumed", error.Code);
    }

    [Fact]
    public void IApprovalRequestRepository_and_its_implementation_are_not_public()
    {
        Assert.False(typeof(IApprovalRequestRepository).IsPublic);
        Assert.False(typeof(InMemoryApprovalRequestRepository).IsPublic);
    }

    [Fact]
    public void ApprovalRequest_has_no_publicly_accessible_constructor()
    {
        Assert.Empty(typeof(ApprovalRequest).GetConstructors(BindingFlags.Public | BindingFlags.Instance));
    }

    // --- Services ---

    [Fact]
    public void RoleAssignmentService_PermissionService_and_MakerCheckerService_are_all_internal()
    {
        Assert.False(typeof(RoleAssignmentService).IsPublic);
        Assert.False(typeof(PermissionService).IsPublic);
        Assert.False(typeof(MakerCheckerService).IsPublic);
    }

    [Fact]
    public void This_project_has_no_assembly_reference_to_BIO_IDN_001s_PersonRegistry_project()
    {
        // This project's own AC02 tenant-scoping and AC01 existence-check both go through
        // IPersonExistenceGateway, never a ProjectReference - confirmed adversarially by
        // inspecting the compiled assembly's own referenced-assembly list.
        var referenced = typeof(RoleAssignmentService).Assembly.GetReferencedAssemblies().Select(a => a.Name);
        Assert.DoesNotContain(referenced, n => n is not null && n.Contains("PersonRegistry", StringComparison.Ordinal));
    }
}

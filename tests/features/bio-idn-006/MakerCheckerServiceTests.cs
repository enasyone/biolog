using Biolog.Features.Rbac.Api;
using Biolog.Features.Rbac.Audit;
using Biolog.Features.Rbac.Domain;
using Biolog.Features.Rbac.Identifiers;
using Xunit;

namespace Biolog.Features.Rbac.Tests;

/// <summary>
/// BIO-IDN-006-AC03: sensitive-action rules and the maker-checker approval workflow. This file
/// carries the story's mandatory privilege-escalation negative tests - separation of duties
/// (maker may never be checker) is the ONE property this whole workflow exists to enforce.
/// </summary>
public sealed class MakerCheckerServiceTests
{
    /// <summary>
    /// BIO-IDN-006-QA-07: raising/deciding a request now requires real authority too - Maker and
    /// Checker are pre-seeded, already-privileged, and DISTINCT SUPER_ADMINs (not the codebase's
    /// usual free-form pre-auth ids like "registrar_ada"), mirroring RoleAssignmentServiceTests's
    /// own established bootstrap-actor convention. Tests that specifically probe UNAUTHORIZED
    /// maker/checker behaviour construct their own separate, deliberately-unprivileged actor
    /// instead (see the PRIVILEGE_ESCALATION tests below).
    /// </summary>
    private const string MakerPersonId = "per_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a95";
    private const string CheckerPersonId = "per_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a96";
    private static readonly AuditActor Maker = new(ActorType.User, MakerPersonId);
    private static readonly AuditActor Checker = new(ActorType.User, CheckerPersonId);
    private const string TenantId = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77";
    private const string TenantB = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a88";
    private const string ActionKey = "person.archive";
    private const string TargetRef = "per_0199e0c1-9d40-7e52-b103-7c9a4e2f6b1d";

    /// <summary>
    /// BIO-IDN-006-QA-04: configuring a sensitive-action rule now requires real authority -
    /// RuleAdmin is the administrator who configures whether the workflow applies to an action in
    /// the first place, a distinct concern from Maker/Checker participating in the workflow
    /// itself, so rule-configuration calls use this separate, pre-seeded SUPER_ADMIN instead.
    /// </summary>
    private const string RuleAdminPersonId = "per_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a98";
    private static readonly AuditActor RuleAdmin = new(ActorType.User, RuleAdminPersonId);

    private static async Task<MakerCheckerService> NewService(
        ISensitiveActionRuleRepository? ruleRepository = null, IApprovalRequestRepository? requestRepository = null, IRbacAuditSink? auditSink = null,
        SensitiveActionRuleLockRegistry? ruleLockRegistry = null, ApprovalRequestLockRegistry? requestLockRegistry = null, string tenantId = TenantId,
        IPersonRoleAssignmentRepository? assignmentRepository = null, PermissionService? permissionService = null, CancellationToken cancellationToken = default)
    {
        var assignments = assignmentRepository ?? new InMemoryPersonRoleAssignmentRepository();
        var gateway = new FakePersonExistenceGateway();
        var sink = auditSink ?? new InMemoryRbacAuditSink();
        var tenantProvider = new FakeCurrentTenantProvider(tenantId);

        foreach (var personId in new[] { RuleAdminPersonId, MakerPersonId, CheckerPersonId })
        {
            if (await assignments.GetAsync(tenantId, personId, RoleType.SuperAdmin, cancellationToken) is null)
            {
                var now = UtcInstant.Now();
                await assignments.CreateAsync(
                    new PersonRoleAssignment(tenantId, personId, RoleType.SuperAdmin) { Status = RoleAssignmentStatus.Active, Version = 1, CreatedAt = now, UpdatedAt = now },
                    cancellationToken);
            }
        }

        var permissions = permissionService ?? new PermissionService(
            tenantProvider, new InMemoryRolePermissionGrantRepository(), assignments, sink, new PermissionGrantLockRegistry(), gateway);

        return new(
            tenantProvider, ruleRepository ?? new InMemorySensitiveActionRuleRepository(), requestRepository ?? new InMemoryApprovalRequestRepository(), sink,
            ruleLockRegistry ?? new SensitiveActionRuleLockRegistry(), requestLockRegistry ?? new ApprovalRequestLockRegistry(), permissions);
    }

    private static RequestApprovalRequest ValidRequest(string reason = "annual archival sweep") => new() { TargetRef = TargetRef, Reason = reason };

    // --- AC03: sensitive-action rule configuration ---

    [Fact]
    public async Task Fail_open_default_an_unconfigured_action_does_not_require_second_approval()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = await NewService();

        var rule = await service.GetActionRuleAsync(ActionKey, ct);

        Assert.Null(rule);
        Assert.True(await service.IsActionClearedAsync(ActionKey, TargetRef, ct)); // nothing configured -> always cleared
    }

    [Fact]
    public async Task Enabling_second_approval_for_an_action_key_persists_and_reads_back()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = await NewService();

        await service.SetActionRuleAsync(ActionKey, new SetSensitiveActionRuleRequest { RequiresSecondApproval = true }, RuleAdmin, CanonicalId.NewEventId(), ct);

        var rule = await service.GetActionRuleAsync(ActionKey, ct);
        Assert.True(rule!.RequiresSecondApproval);
        Assert.False(await service.IsActionClearedAsync(ActionKey, TargetRef, ct)); // required, but never requested -> not cleared
    }

    [Fact]
    public async Task AC02_style_a_rule_configured_in_tenant_A_never_applies_in_tenant_B()
    {
        var ct = TestContext.Current.CancellationToken;
        var rules = new InMemorySensitiveActionRuleRepository();
        var serviceA = await NewService(rules, tenantId: TenantId);
        var serviceB = await NewService(rules, tenantId: TenantB);
        await serviceA.SetActionRuleAsync(ActionKey, new SetSensitiveActionRuleRequest { RequiresSecondApproval = true }, RuleAdmin, CanonicalId.NewEventId(), ct);

        var ruleInB = await serviceB.GetActionRuleAsync(ActionKey, ct);

        Assert.Null(ruleInB);
    }

    // --- BIO-IDN-006-QA-04 (Seyi round 2, Critical): configuring the rule itself requires real authority ---

    [Fact]
    public async Task PRIVILEGE_ESCALATION_an_unprivileged_actor_cannot_configure_or_disable_a_sensitive_action_rule()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = await NewService();
        var unprivilegedActor = new AuditActor(ActorType.User, "unprivileged_attacker");

        var error = await Assert.ThrowsAsync<RbacError>(() => service.SetActionRuleAsync(
            ActionKey, new SetSensitiveActionRuleRequest { RequiresSecondApproval = false }, unprivilegedActor, CanonicalId.NewEventId(), ct));

        Assert.Equal("permission_required", error.Code);
    }

    [Fact]
    public async Task A_role_explicitly_delegated_rbac_sensitive_action_rule_manage_can_configure_the_rule_without_holding_SUPER_ADMIN()
    {
        var ct = TestContext.Current.CancellationToken;
        var assignments = new InMemoryPersonRoleAssignmentRepository();
        var tenantProvider = new FakeCurrentTenantProvider(TenantId);
        var permissions = new PermissionService(
            tenantProvider, new InMemoryRolePermissionGrantRepository(), assignments, new InMemoryRbacAuditSink(), new PermissionGrantLockRegistry(), new FakePersonExistenceGateway());
        var service = await NewService(assignmentRepository: assignments, permissionService: permissions);
        const string delegatedPersonId = "per_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a22";
        var now = UtcInstant.Now();
        await assignments.CreateAsync(
            new PersonRoleAssignment(TenantId, delegatedPersonId, RoleType.Security) { Status = RoleAssignmentStatus.Active, Version = 1, CreatedAt = now, UpdatedAt = now }, ct);
        await permissions.SetGrantAsync(
            RoleType.Security, MakerCheckerService.SensitiveActionRuleManagePermission, new SetPermissionGrantRequest { Granted = true }, RuleAdmin, CanonicalId.NewEventId(), ct);
        var delegatedActor = new AuditActor(ActorType.User, delegatedPersonId);

        var rule = await service.SetActionRuleAsync(ActionKey, new SetSensitiveActionRuleRequest { RequiresSecondApproval = true }, delegatedActor, CanonicalId.NewEventId(), ct);

        Assert.True(rule.RequiresSecondApproval);
    }

    // --- BIO-IDN-006-QA-05 (Seyi round 2, High): an approval is single-use, never permanently reusable ---

    [Fact]
    public async Task An_approved_requests_clearance_is_consumed_by_the_first_mutation_and_never_clears_a_second()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = await NewService();
        await service.SetActionRuleAsync(ActionKey, new SetSensitiveActionRuleRequest { RequiresSecondApproval = true }, RuleAdmin, CanonicalId.NewEventId(), ct);
        var request = await service.RequestApprovalAsync(ActionKey, ValidRequest(), Maker, CanonicalId.NewEventId(), ct);
        await service.DecideAsync(
            ActionKey, TargetRef, request.SequenceNumber, new DecideApprovalRequest { Decision = "APPROVED", ReasonCode = "verified", Version = request.Version },
            Checker, CanonicalId.NewEventId(), ct);

        var firstConsume = await service.TryConsumeApprovalAsync(ActionKey, TargetRef, ct);
        var secondConsume = await service.TryConsumeApprovalAsync(ActionKey, TargetRef, ct);

        Assert.True(firstConsume); // the mutation this approval was raised for
        Assert.False(secondConsume); // a later, unrelated mutation for the same target cannot reuse it
    }

    [Fact]
    public async Task A_fresh_approval_is_required_and_does_work_for_a_second_mutation_of_the_same_target()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = await NewService();
        await service.SetActionRuleAsync(ActionKey, new SetSensitiveActionRuleRequest { RequiresSecondApproval = true }, RuleAdmin, CanonicalId.NewEventId(), ct);
        var first = await service.RequestApprovalAsync(ActionKey, ValidRequest(), Maker, CanonicalId.NewEventId(), ct);
        await service.DecideAsync(
            ActionKey, TargetRef, first.SequenceNumber, new DecideApprovalRequest { Decision = "APPROVED", ReasonCode = "verified", Version = first.Version },
            Checker, CanonicalId.NewEventId(), ct);
        Assert.True(await service.TryConsumeApprovalAsync(ActionKey, TargetRef, ct)); // consumes the first approval

        var second = await service.RequestApprovalAsync(ActionKey, ValidRequest("a second, unrelated mutation"), Maker, CanonicalId.NewEventId(), ct);
        await service.DecideAsync(
            ActionKey, TargetRef, second.SequenceNumber, new DecideApprovalRequest { Decision = "APPROVED", ReasonCode = "verified", Version = second.Version },
            Checker, CanonicalId.NewEventId(), ct);

        Assert.True(await service.TryConsumeApprovalAsync(ActionKey, TargetRef, ct)); // the fresh approval clears the second mutation
    }

    // --- AC03: the maker-checker workflow ---

    [Fact]
    public async Task Requesting_approval_creates_a_pending_request_at_sequence_1()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = await NewService();

        var request = await service.RequestApprovalAsync(ActionKey, ValidRequest(), Maker, CanonicalId.NewEventId(), ct);

        Assert.Equal(ApprovalRequestStatus.Pending, request.Status);
        Assert.Equal(1, request.SequenceNumber);
        Assert.Equal(Maker.Id, request.RequestedByActorId);
    }

    [Fact]
    public async Task A_second_request_for_the_same_target_while_one_is_pending_is_rejected()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = await NewService();
        await service.RequestApprovalAsync(ActionKey, ValidRequest(), Maker, CanonicalId.NewEventId(), ct);

        var error = await Assert.ThrowsAsync<RbacError>(() => service.RequestApprovalAsync(ActionKey, ValidRequest(), Maker, CanonicalId.NewEventId(), ct));

        Assert.Equal("approval_request_already_pending", error.Code);
    }

    [Fact]
    public async Task A_new_request_can_be_raised_after_the_prior_one_was_decided()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = await NewService();
        var first = await service.RequestApprovalAsync(ActionKey, ValidRequest(), Maker, CanonicalId.NewEventId(), ct);
        await service.DecideAsync(ActionKey, TargetRef, first.SequenceNumber, new DecideApprovalRequest { Decision = "REJECTED", ReasonCode = "insufficient evidence", Version = first.Version }, Checker, CanonicalId.NewEventId(), ct);

        var second = await service.RequestApprovalAsync(ActionKey, ValidRequest("resubmission with more evidence"), Maker, CanonicalId.NewEventId(), ct);

        Assert.Equal(2, second.SequenceNumber); // full history preserved, new generation at the next sequence
    }

    [Fact]
    public async Task Approving_a_request_records_the_checker_and_reason()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = await NewService();
        var request = await service.RequestApprovalAsync(ActionKey, ValidRequest(), Maker, CanonicalId.NewEventId(), ct);

        var decided = await service.DecideAsync(
            ActionKey, TargetRef, request.SequenceNumber, new DecideApprovalRequest { Decision = "APPROVED", ReasonCode = "verified", Version = request.Version },
            Checker, CanonicalId.NewEventId(), ct);

        Assert.Equal(ApprovalRequestStatus.Approved, decided.Status);
        Assert.Equal(Checker.Id, decided.DecidedByActorId);
        Assert.NotNull(decided.DecidedAt);
    }

    [Fact]
    public async Task An_approved_request_clears_the_action_for_that_target()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = await NewService();
        await service.SetActionRuleAsync(ActionKey, new SetSensitiveActionRuleRequest { RequiresSecondApproval = true }, RuleAdmin, CanonicalId.NewEventId(), ct);
        var request = await service.RequestApprovalAsync(ActionKey, ValidRequest(), Maker, CanonicalId.NewEventId(), ct);
        await service.DecideAsync(
            ActionKey, TargetRef, request.SequenceNumber, new DecideApprovalRequest { Decision = "APPROVED", ReasonCode = "verified", Version = request.Version },
            Checker, CanonicalId.NewEventId(), ct);

        Assert.True(await service.IsActionClearedAsync(ActionKey, TargetRef, ct));
    }

    [Fact]
    public async Task A_rejected_request_never_clears_the_action()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = await NewService();
        await service.SetActionRuleAsync(ActionKey, new SetSensitiveActionRuleRequest { RequiresSecondApproval = true }, RuleAdmin, CanonicalId.NewEventId(), ct);
        var request = await service.RequestApprovalAsync(ActionKey, ValidRequest(), Maker, CanonicalId.NewEventId(), ct);

        await service.DecideAsync(
            ActionKey, TargetRef, request.SequenceNumber, new DecideApprovalRequest { Decision = "REJECTED", ReasonCode = "not justified", Version = request.Version },
            Checker, CanonicalId.NewEventId(), ct);

        Assert.False(await service.IsActionClearedAsync(ActionKey, TargetRef, ct));
    }

    // --- BIO-IDN-006-QA-07 (Seyi round 4, Critical): raising/deciding a request requires real authority ---

    [Fact]
    public async Task PRIVILEGE_ESCALATION_an_unprivileged_actor_cannot_raise_an_approval_request()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = await NewService();
        var unprivilegedActor = new AuditActor(ActorType.User, "unprivileged_attacker");

        var error = await Assert.ThrowsAsync<RbacError>(() => service.RequestApprovalAsync(ActionKey, ValidRequest(), unprivilegedActor, CanonicalId.NewEventId(), ct));

        Assert.Equal("permission_required", error.Code);
    }

    [Fact]
    public async Task PRIVILEGE_ESCALATION_an_unprivileged_actor_cannot_decide_an_approval_request()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = await NewService();
        var request = await service.RequestApprovalAsync(ActionKey, ValidRequest(), Maker, CanonicalId.NewEventId(), ct);
        var unprivilegedActor = new AuditActor(ActorType.User, "unprivileged_attacker");

        var error = await Assert.ThrowsAsync<RbacError>(() => service.DecideAsync(
            ActionKey, TargetRef, request.SequenceNumber, new DecideApprovalRequest { Decision = "APPROVED", ReasonCode = "verified", Version = request.Version },
            unprivilegedActor, CanonicalId.NewEventId(), ct));

        Assert.Equal("permission_required", error.Code);
    }

    [Fact]
    public async Task A_role_explicitly_delegated_rbac_approval_request_and_rbac_approval_decide_can_participate_without_holding_SUPER_ADMIN()
    {
        var ct = TestContext.Current.CancellationToken;
        var assignments = new InMemoryPersonRoleAssignmentRepository();
        var tenantProvider = new FakeCurrentTenantProvider(TenantId);
        var permissions = new PermissionService(
            tenantProvider, new InMemoryRolePermissionGrantRepository(), assignments, new InMemoryRbacAuditSink(), new PermissionGrantLockRegistry(), new FakePersonExistenceGateway());
        var service = await NewService(assignmentRepository: assignments, permissionService: permissions);
        const string delegatedMakerId = "per_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a25";
        const string delegatedCheckerId = "per_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a26";
        var now = UtcInstant.Now();
        await assignments.CreateAsync(
            new PersonRoleAssignment(TenantId, delegatedMakerId, RoleType.RegistrarHr) { Status = RoleAssignmentStatus.Active, Version = 1, CreatedAt = now, UpdatedAt = now }, ct);
        await assignments.CreateAsync(
            new PersonRoleAssignment(TenantId, delegatedCheckerId, RoleType.TenantAdmin) { Status = RoleAssignmentStatus.Active, Version = 1, CreatedAt = now, UpdatedAt = now }, ct);
        await permissions.SetGrantAsync(
            RoleType.RegistrarHr, MakerCheckerService.ApprovalRequestPermission, new SetPermissionGrantRequest { Granted = true }, RuleAdmin, CanonicalId.NewEventId(), ct);
        await permissions.SetGrantAsync(
            RoleType.TenantAdmin, MakerCheckerService.ApprovalDecidePermission, new SetPermissionGrantRequest { Granted = true }, RuleAdmin, CanonicalId.NewEventId(), ct);
        var delegatedMaker = new AuditActor(ActorType.User, delegatedMakerId);
        var delegatedChecker = new AuditActor(ActorType.User, delegatedCheckerId);

        var request = await service.RequestApprovalAsync(ActionKey, ValidRequest(), delegatedMaker, CanonicalId.NewEventId(), ct);
        var decided = await service.DecideAsync(
            ActionKey, TargetRef, request.SequenceNumber, new DecideApprovalRequest { Decision = "APPROVED", ReasonCode = "verified", Version = request.Version },
            delegatedChecker, CanonicalId.NewEventId(), ct);

        Assert.Equal(ApprovalRequestStatus.Approved, decided.Status);
    }

    // --- PRIVILEGE ESCALATION: mandatory adversarial tests (AC03's core security property) ---

    [Fact]
    public async Task PRIVILEGE_ESCALATION_approving_ones_own_request_is_rejected()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = await NewService();
        var request = await service.RequestApprovalAsync(ActionKey, ValidRequest(), Maker, CanonicalId.NewEventId(), ct);

        var error = await Assert.ThrowsAsync<RbacError>(() => service.DecideAsync(
            ActionKey, TargetRef, request.SequenceNumber, new DecideApprovalRequest { Decision = "APPROVED", ReasonCode = "self-serve", Version = request.Version },
            Maker, CanonicalId.NewEventId(), ct));

        Assert.Equal("self_approval_forbidden", error.Code);
    }

    [Fact]
    public async Task PRIVILEGE_ESCALATION_rejecting_ones_own_request_is_also_forbidden_not_only_approving()
    {
        // Separation of duties bars the maker from deciding their own request in EITHER
        // direction - a maker who could reject their own request could also spam-resubmit
        // until a favourable checker happens to pick it up, defeating the control's purpose.
        var ct = TestContext.Current.CancellationToken;
        var service = await NewService();
        var request = await service.RequestApprovalAsync(ActionKey, ValidRequest(), Maker, CanonicalId.NewEventId(), ct);

        var error = await Assert.ThrowsAsync<RbacError>(() => service.DecideAsync(
            ActionKey, TargetRef, request.SequenceNumber, new DecideApprovalRequest { Decision = "REJECTED", ReasonCode = "self-serve", Version = request.Version },
            Maker, CanonicalId.NewEventId(), ct));

        Assert.Equal("self_approval_forbidden", error.Code);
    }

    [Fact]
    public async Task PRIVILEGE_ESCALATION_self_approval_is_rejected_before_any_state_changes()
    {
        var ct = TestContext.Current.CancellationToken;
        var repository = new InMemoryApprovalRequestRepository();
        var service = await NewService(requestRepository: repository);
        var request = await service.RequestApprovalAsync(ActionKey, ValidRequest(), Maker, CanonicalId.NewEventId(), ct);

        await Assert.ThrowsAsync<RbacError>(() => service.DecideAsync(
            ActionKey, TargetRef, request.SequenceNumber, new DecideApprovalRequest { Decision = "APPROVED", ReasonCode = "self-serve", Version = request.Version },
            Maker, CanonicalId.NewEventId(), ct));

        var stillPending = await repository.GetAsync(TenantId, ActionKey, TargetRef, request.SequenceNumber, ct);
        Assert.Equal(ApprovalRequestStatus.Pending, stillPending!.Status);
        Assert.Null(stillPending.DecidedByActorId);
    }

    [Fact]
    public async Task PRIVILEGE_ESCALATION_a_forged_replacement_record_cannot_smuggle_a_different_decider_past_the_repository_boundary()
    {
        // Even if a caller bypassed the service entirely and called the repository directly
        // with a crafted "updated" record claiming a DIFFERENT decider, ReplaceAsync checks the
        // ACTUAL deciding actor it is given against the ORIGINAL request's maker - not anything
        // embedded in the replacement record, which a caller could otherwise set to whatever it
        // wants.
        var ct = TestContext.Current.CancellationToken;
        var repository = new InMemoryApprovalRequestRepository();
        var original = await repository.CreateAsync(
            new ApprovalRequest(TenantId, ActionKey, TargetRef, 1)
            { Status = ApprovalRequestStatus.Pending, RequestedByActorId = Maker.Id, RequestReason = "x", Version = 1, CreatedAt = UtcInstant.Now(), UpdatedAt = UtcInstant.Now() },
            ct);
        var forged = original with { Status = ApprovalRequestStatus.Approved, DecidedByActorId = "someone_else", Version = 2 };

        // Passing the MAKER's own id as the actual decider - the attack this defends against.
        var error = await Assert.ThrowsAsync<RbacError>(() => repository.ReplaceAsync(original, forged, Maker.Id, ct));

        Assert.Equal("self_approval_forbidden", error.Code);
    }

    [Fact]
    public async Task Deciding_an_already_decided_request_is_rejected_not_silently_absorbed()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = await NewService();
        var request = await service.RequestApprovalAsync(ActionKey, ValidRequest(), Maker, CanonicalId.NewEventId(), ct);
        var decided = await service.DecideAsync(
            ActionKey, TargetRef, request.SequenceNumber, new DecideApprovalRequest { Decision = "APPROVED", ReasonCode = "verified", Version = request.Version },
            Checker, CanonicalId.NewEventId(), ct);

        var error = await Assert.ThrowsAsync<RbacError>(() => service.DecideAsync(
            ActionKey, TargetRef, request.SequenceNumber, new DecideApprovalRequest { Decision = "REJECTED", ReasonCode = "second opinion", Version = decided.Version },
            Checker, CanonicalId.NewEventId(), ct));

        Assert.Equal("approval_request_already_decided", error.Code);
    }

    [Fact]
    public async Task Deciding_with_a_stale_version_is_a_concurrency_conflict()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = await NewService();
        var request = await service.RequestApprovalAsync(ActionKey, ValidRequest(), Maker, CanonicalId.NewEventId(), ct);

        var error = await Assert.ThrowsAsync<RbacError>(() => service.DecideAsync(
            ActionKey, TargetRef, request.SequenceNumber, new DecideApprovalRequest { Decision = "APPROVED", ReasonCode = "verified", Version = request.Version + 1 },
            Checker, CanonicalId.NewEventId(), ct));

        Assert.Equal("concurrency_conflict", error.Code);
    }

    [Fact]
    public async Task Deciding_a_never_requested_target_is_404()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = await NewService();

        var error = await Assert.ThrowsAsync<RbacError>(() => service.DecideAsync(
            ActionKey, TargetRef, 1, new DecideApprovalRequest { Decision = "APPROVED", ReasonCode = "x", Version = 1 }, Checker, CanonicalId.NewEventId(), ct));

        Assert.Equal("approval_request_not_found", error.Code);
    }

    [Fact]
    public async Task Deciding_without_a_reason_code_is_rejected()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = await NewService();
        var request = await service.RequestApprovalAsync(ActionKey, ValidRequest(), Maker, CanonicalId.NewEventId(), ct);

        var error = await Assert.ThrowsAsync<RbacError>(() => service.DecideAsync(
            ActionKey, TargetRef, request.SequenceNumber, new DecideApprovalRequest { Decision = "APPROVED", Version = request.Version }, Checker, CanonicalId.NewEventId(), ct));

        Assert.Equal("validation_failed", error.Code);
    }

    [Fact]
    public async Task AC02_style_approval_history_raised_in_tenant_A_is_invisible_in_tenant_B()
    {
        var ct = TestContext.Current.CancellationToken;
        var requests = new InMemoryApprovalRequestRepository();
        var serviceA = await NewService(requestRepository: requests, tenantId: TenantId);
        var serviceB = await NewService(requestRepository: requests, tenantId: TenantB);
        await serviceA.RequestApprovalAsync(ActionKey, ValidRequest(), Maker, CanonicalId.NewEventId(), ct);

        var historyInB = await serviceB.ListHistoryAsync(ActionKey, TargetRef, ct);

        Assert.Empty(historyInB);
    }

    // --- ADR-0003 Decision 7: audit-atomicity ---

    [Fact]
    public async Task RequestApproval_records_an_audit_event()
    {
        var ct = TestContext.Current.CancellationToken;
        var sink = new InMemoryRbacAuditSink();
        var service = await NewService(auditSink: sink);

        await service.RequestApprovalAsync(ActionKey, ValidRequest(), Maker, CanonicalId.NewEventId(), ct);

        Assert.Contains(sink.Events, e => e.Action == "rbac.approval_request");
    }

    [Fact]
    public async Task Decide_records_an_audit_event()
    {
        var ct = TestContext.Current.CancellationToken;
        var sink = new InMemoryRbacAuditSink();
        var service = await NewService(auditSink: sink);
        var request = await service.RequestApprovalAsync(ActionKey, ValidRequest(), Maker, CanonicalId.NewEventId(), ct);

        await service.DecideAsync(
            ActionKey, TargetRef, request.SequenceNumber, new DecideApprovalRequest { Decision = "APPROVED", ReasonCode = "verified", Version = request.Version },
            Checker, CanonicalId.NewEventId(), ct);

        Assert.Contains(sink.Events, e => e.Action == "rbac.approval_decide");
    }

    [Fact]
    public async Task RequestApproval_is_never_committed_when_the_audit_sink_fails()
    {
        var ct = TestContext.Current.CancellationToken;
        var repository = new InMemoryApprovalRequestRepository();
        var service = await NewService(requestRepository: repository, auditSink: new ThrowingAuditSink());

        var error = await Assert.ThrowsAsync<RbacError>(() => service.RequestApprovalAsync(ActionKey, ValidRequest(), Maker, CanonicalId.NewEventId(), ct));

        Assert.Equal("audit_unavailable", error.Code);
        Assert.Empty(await repository.ListHistoryAsync(TenantId, ActionKey, TargetRef, ct));
    }

    [Fact]
    public async Task Decide_is_never_committed_when_the_audit_sink_fails()
    {
        var ct = TestContext.Current.CancellationToken;
        var repository = new InMemoryApprovalRequestRepository();
        var request = await (await NewService(requestRepository: repository)).RequestApprovalAsync(ActionKey, ValidRequest(), Maker, CanonicalId.NewEventId(), ct);
        var service = await NewService(requestRepository: repository, auditSink: new ThrowingAuditSink());

        var error = await Assert.ThrowsAsync<RbacError>(() => service.DecideAsync(
            ActionKey, TargetRef, request.SequenceNumber, new DecideApprovalRequest { Decision = "APPROVED", ReasonCode = "verified", Version = request.Version },
            Checker, CanonicalId.NewEventId(), ct));

        Assert.Equal("audit_unavailable", error.Code);
        var stillPending = await repository.GetAsync(TenantId, ActionKey, TargetRef, request.SequenceNumber, ct);
        Assert.Equal(ApprovalRequestStatus.Pending, stillPending!.Status);
    }

    private sealed class ThrowingAuditSink : IRbacAuditSink
    {
        public Task WriteAsync(RbacAuditEvent auditEvent, CancellationToken cancellationToken = default) => throw new InvalidOperationException("synthetic sink outage");

        public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("synthetic sink outage: cannot even verify");
    }
}

using Biolog.Features.AccessPolicy.Api;
using Biolog.Features.AccessPolicy.Audit;
using Biolog.Features.AccessPolicy.Domain;
using Xunit;

namespace Biolog.Features.AccessPolicy.Tests;

/// <summary>
/// Unit tests for <see cref="AccessPolicyService"/> - BIO-ACC-001-AC01 (the policy model's own
/// dimensions), AC02 (draft/publish versioning) and AC04 (audit-atomicity) at the service level.
/// AC03 (deny precedence) is tested directly against the pure <see cref="PolicyEvaluator"/> in
/// <c>PolicyEvaluationServiceTests.cs</c>.
/// </summary>
public sealed class AccessPolicyServiceTests
{
    private const string TenantA = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77";
    private const string AuthorActorId = "per_0199e0c1-9d40-7e52-b103-7c9a4e2f6b1d";
    private const string PublisherActorId = "per_0199e0c1-9d40-7e52-b103-7c9a4e2f6b1e";
    private const string SubjectPersonId = "per_0199e0c1-9d40-7e52-b103-7c9a4e2f6b1f";
    private static readonly AuditActor Author = new(ActorType.User, AuthorActorId);
    private static readonly AuditActor Publisher = new(ActorType.User, PublisherActorId);

    private static PolicyRuleRequest FullRuleRequest(string? subjectPersonId = null) => new()
    {
        Effect = "ALLOW",
        Subject = subjectPersonId is null
            ? new SubjectMatchRequest { Kind = "ANY_PERSON" }
            : new SubjectMatchRequest { Kind = "SPECIFIC_PERSON", Value = subjectPersonId },
        Credential = new CredentialClassMatchRequest { Kind = "SPECIFIC_CLASS", CredentialClass = "staff-card" },
        Location = new LocationMatchRequest { Kind = "SPECIFIC_LOCATION", LocationId = "main-gate" },
        Schedule = new ScheduleMatchRequest
        {
            Kind = "WINDOWS",
            Windows = [new ScheduleWindowRequest { DayOfWeek = "MONDAY", StartTime = "08:00", EndTime = "18:00" }],
        },
        IdentityStatus = new IdentityStatusMatchRequest { AnyStatus = false, AllowedStatuses = ["ACTIVE"] },
        CardStatus = new CardStatusMatchRequest { AnyStatus = false, AllowedStatuses = ["active"] },
        RequiredAssuranceLevel = "BASIC",
    };

    private static (AccessPolicyService Service, InMemoryAccessPolicyRepository Repository, InMemoryAccessPolicyAuditSink AuditSink) BuildService(
        IReadOnlySet<string>? existingPersonIds = null, IReadOnlySet<(string, string)>? grantedPermissions = null)
    {
        var repository = new InMemoryAccessPolicyRepository();
        var auditSink = new InMemoryAccessPolicyAuditSink();
        var service = new AccessPolicyService(
            new FakeCurrentTenantProvider(TenantA), repository, auditSink, new AccessPolicyLockRegistry(),
            new FakePersonExistenceGateway(existingPersonIds), new FakePermissionGateway(grantedPermissions));
        return (service, repository, auditSink);
    }

    // --- AC01: the policy model supports every named dimension ---

    [Fact]
    public async Task Creating_a_draft_with_a_rule_naming_every_AC01_dimension_succeeds()
    {
        var ct = TestContext.Current.CancellationToken;
        var (service, _, _) = BuildService();

        var policy = await service.CreateDraftAsync(
            new CreateAccessPolicyRequest { PolicyKey = "main-campus", Rules = [FullRuleRequest(SubjectPersonId)] }, Author, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77", ct);

        Assert.Equal(1, policy.Latest.VersionNumber);
        Assert.Equal(AccessPolicyStatus.Draft, policy.Latest.Status);
        var rule = Assert.Single(policy.Latest.Rules);
        Assert.Equal(SubjectMatchKind.SpecificPerson, rule.Subject.Kind);
        Assert.Equal("staff-card", rule.Credential.CredentialClass);
        Assert.Equal("main-gate", rule.Location.LocationId);
        Assert.Equal(DayOfWeek.Monday, rule.Schedule.Windows[0].DayOfWeek);
        Assert.Contains(IdentityStatus.Active, rule.IdentityStatus.AllowedStatuses);
        Assert.Contains(CardStatus.Active, rule.CardStatus.AllowedStatuses);
        Assert.Equal(AssuranceLevel.Basic, rule.RequiredAssuranceLevel);
    }

    [Fact]
    public async Task A_rule_naming_a_specific_subject_that_does_not_exist_is_rejected()
    {
        var ct = TestContext.Current.CancellationToken;
        var (service, _, _) = BuildService(existingPersonIds: new HashSet<string>());

        var error = await Assert.ThrowsAsync<AccessPolicyError>(() => service.CreateDraftAsync(
            new CreateAccessPolicyRequest { PolicyKey = "main-campus", Rules = [FullRuleRequest(SubjectPersonId)] }, Author, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77", ct));

        Assert.Equal("subject_person_not_found", error.Code);
    }

    [Fact]
    public async Task A_rule_with_no_rules_at_all_is_rejected_as_a_validation_failure()
    {
        var ct = TestContext.Current.CancellationToken;
        var (service, _, _) = BuildService();

        var error = await Assert.ThrowsAsync<AccessPolicyError>(() => service.CreateDraftAsync(
            new CreateAccessPolicyRequest { PolicyKey = "main-campus", Rules = [] }, Author, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77", ct));

        Assert.Equal("validation_failed", error.Code);
    }

    [Fact]
    public async Task An_overnight_spanning_schedule_window_is_rejected()
    {
        var ct = TestContext.Current.CancellationToken;
        var (service, _, _) = BuildService();
        var rule = FullRuleRequest() with
        {
            Schedule = new ScheduleMatchRequest { Kind = "WINDOWS", Windows = [new ScheduleWindowRequest { DayOfWeek = "FRIDAY", StartTime = "22:00", EndTime = "06:00" }] },
        };

        var error = await Assert.ThrowsAsync<AccessPolicyError>(() => service.CreateDraftAsync(
            new CreateAccessPolicyRequest { PolicyKey = "main-campus", Rules = [rule] }, Author, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77", ct));

        Assert.Equal("validation_failed", error.Code);
    }

    // --- AC02: draft/publish versioning ---

    [Fact]
    public async Task Revising_a_draft_edits_the_same_version_in_place()
    {
        var ct = TestContext.Current.CancellationToken;
        var (service, _, _) = BuildService();
        var created = await service.CreateDraftAsync(
            new CreateAccessPolicyRequest { PolicyKey = "main-campus", Rules = [FullRuleRequest()] }, Author, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77", ct);

        var revised = await service.ReviseDraftAsync(
            "main-campus", new ReviseDraftRequest { Version = created.ConcurrencyVersion, Rules = [FullRuleRequest() with { RequiredAssuranceLevel = "HIGH" }] },
            Author, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a78", ct);

        Assert.Equal(1, revised.Latest.VersionNumber); // still version 1 - edited in place while Draft
        Assert.Equal(AssuranceLevel.High, revised.Latest.Rules[0].RequiredAssuranceLevel);
    }

    [Fact]
    public async Task Revising_a_published_policy_starts_a_new_draft_version_leaving_the_published_one_untouched()
    {
        var ct = TestContext.Current.CancellationToken;
        var (service, _, _) = BuildService();
        var created = await service.CreateDraftAsync(
            new CreateAccessPolicyRequest { PolicyKey = "main-campus", Rules = [FullRuleRequest()] }, Author, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77", ct);
        var published = await service.PublishAsync("main-campus", new PublishAccessPolicyRequest { Version = created.ConcurrencyVersion }, Publisher, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a78", ct);

        var revised = await service.ReviseDraftAsync(
            "main-campus", new ReviseDraftRequest { Version = published.ConcurrencyVersion, Rules = [FullRuleRequest() with { RequiredAssuranceLevel = "HIGH" }] },
            Author, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a79", ct);

        Assert.Equal(2, revised.Latest.VersionNumber);
        Assert.Equal(AccessPolicyStatus.Draft, revised.Latest.Status);
        var publishedVersion = revised.GetPublishedVersion();
        Assert.NotNull(publishedVersion);
        Assert.Equal(1, publishedVersion!.VersionNumber);
        Assert.Equal(AssuranceLevel.Basic, publishedVersion.Rules[0].RequiredAssuranceLevel); // unchanged
    }

    [Fact]
    public async Task Publishing_sets_the_published_version_number_and_records_the_publisher()
    {
        var ct = TestContext.Current.CancellationToken;
        var (service, _, _) = BuildService();
        var created = await service.CreateDraftAsync(
            new CreateAccessPolicyRequest { PolicyKey = "main-campus", Rules = [FullRuleRequest()] }, Author, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77", ct);

        var published = await service.PublishAsync("main-campus", new PublishAccessPolicyRequest { Version = created.ConcurrencyVersion }, Publisher, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a78", ct);

        Assert.Equal(1, published.PublishedVersionNumber);
        Assert.Equal(AccessPolicyStatus.Published, published.Latest.Status);
        Assert.Equal(PublisherActorId, published.Latest.PublishedByActorId);
    }

    [Fact]
    public async Task Publishing_by_the_drafts_own_author_is_rejected_maker_checker()
    {
        var ct = TestContext.Current.CancellationToken;
        var (service, _, _) = BuildService();
        var created = await service.CreateDraftAsync(
            new CreateAccessPolicyRequest { PolicyKey = "main-campus", Rules = [FullRuleRequest()] }, Author, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77", ct);

        var error = await Assert.ThrowsAsync<AccessPolicyError>(() => service.PublishAsync(
            "main-campus", new PublishAccessPolicyRequest { Version = created.ConcurrencyVersion }, Author, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a78", ct));

        Assert.Equal("self_approval_forbidden", error.Code);
    }

    [Fact]
    public async Task Publishing_against_a_stale_expected_version_is_rejected()
    {
        var ct = TestContext.Current.CancellationToken;
        var (service, _, _) = BuildService();
        await service.CreateDraftAsync(new CreateAccessPolicyRequest { PolicyKey = "main-campus", Rules = [FullRuleRequest()] }, Author, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77", ct);

        var error = await Assert.ThrowsAsync<AccessPolicyError>(() => service.PublishAsync(
            "main-campus", new PublishAccessPolicyRequest { Version = 99 }, Publisher, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a78", ct));

        Assert.Equal("concurrency_conflict", error.Code);
    }

    [Fact]
    public async Task Publishing_an_already_published_version_a_second_time_is_rejected()
    {
        var ct = TestContext.Current.CancellationToken;
        var (service, _, _) = BuildService();
        var created = await service.CreateDraftAsync(new CreateAccessPolicyRequest { PolicyKey = "main-campus", Rules = [FullRuleRequest()] }, Author, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77", ct);
        var published = await service.PublishAsync("main-campus", new PublishAccessPolicyRequest { Version = created.ConcurrencyVersion }, Publisher, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a78", ct);

        var error = await Assert.ThrowsAsync<AccessPolicyError>(() => service.PublishAsync(
            "main-campus", new PublishAccessPolicyRequest { Version = published.ConcurrencyVersion }, Publisher, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a79", ct));

        Assert.Equal("policy_version_not_draft", error.Code);
    }

    [Fact]
    public async Task Creating_a_second_draft_with_an_already_used_policy_key_is_rejected()
    {
        var ct = TestContext.Current.CancellationToken;
        var (service, _, _) = BuildService();
        await service.CreateDraftAsync(new CreateAccessPolicyRequest { PolicyKey = "main-campus", Rules = [FullRuleRequest()] }, Author, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77", ct);

        var error = await Assert.ThrowsAsync<AccessPolicyError>(() => service.CreateDraftAsync(
            new CreateAccessPolicyRequest { PolicyKey = "main-campus", Rules = [FullRuleRequest()] }, Author, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a78", ct));

        Assert.Equal("policy_already_exists", error.Code);
    }

    // --- RBAC integration ---

    [Fact]
    public async Task Creating_a_draft_without_the_manage_permission_is_rejected()
    {
        var ct = TestContext.Current.CancellationToken;
        var (service, _, _) = BuildService(grantedPermissions: new HashSet<(string, string)>());

        var error = await Assert.ThrowsAsync<AccessPolicyError>(() => service.CreateDraftAsync(
            new CreateAccessPolicyRequest { PolicyKey = "main-campus", Rules = [FullRuleRequest()] }, Author, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77", ct));

        Assert.Equal("permission_required", error.Code);
    }

    [Fact]
    public async Task Publishing_without_the_publish_permission_is_rejected_even_when_the_actor_holds_manage()
    {
        var ct = TestContext.Current.CancellationToken;
        var (service, repository, _) = BuildService(grantedPermissions: new HashSet<(string, string)> { (AuthorActorId, AccessPolicyService.ManagePermission), (PublisherActorId, AccessPolicyService.ManagePermission) });
        var created = await service.CreateDraftAsync(new CreateAccessPolicyRequest { PolicyKey = "main-campus", Rules = [FullRuleRequest()] }, Author, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77", ct);

        var error = await Assert.ThrowsAsync<AccessPolicyError>(() => service.PublishAsync(
            "main-campus", new PublishAccessPolicyRequest { Version = created.ConcurrencyVersion }, Publisher, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a78", ct));

        Assert.Equal("permission_required", error.Code);
        Assert.Equal(AccessPolicyStatus.Draft, (await repository.GetAsync(TenantA, "main-campus", ct))!.Latest.Status);
    }

    // --- BIO-ACC-001-QA-01 (Chioma's acceptance audit, High): reads require their own permission too, since a policy's content (named subjects, deny rules, schedules) is itself the sensitive asset ---

    [Fact]
    public async Task Getting_a_policy_without_the_read_permission_is_rejected_even_for_the_policys_own_author()
    {
        var ct = TestContext.Current.CancellationToken;
        var (service, _, _) = BuildService(grantedPermissions: new HashSet<(string, string)> { (AuthorActorId, AccessPolicyService.ManagePermission) });
        await service.CreateDraftAsync(new CreateAccessPolicyRequest { PolicyKey = "main-campus", Rules = [FullRuleRequest()] }, Author, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77", ct);

        var error = await Assert.ThrowsAsync<AccessPolicyError>(() => service.GetAsync("main-campus", Author, ct));

        Assert.Equal("permission_required", error.Code);
    }

    [Fact]
    public async Task Listing_policies_without_the_read_permission_is_rejected()
    {
        var ct = TestContext.Current.CancellationToken;
        var (service, _, _) = BuildService(grantedPermissions: new HashSet<(string, string)>());

        var error = await Assert.ThrowsAsync<AccessPolicyError>(() => service.ListAsync(Author, ct));

        Assert.Equal("permission_required", error.Code);
    }

    [Fact]
    public async Task A_caller_holding_only_the_read_permission_can_get_and_list_but_not_author_or_publish()
    {
        var ct = TestContext.Current.CancellationToken;
        var readOnlyActor = new AuditActor(ActorType.User, SubjectPersonId);
        var (service, repository, _) = BuildService(grantedPermissions: new HashSet<(string, string)>
        {
            (AuthorActorId, AccessPolicyService.ManagePermission), (SubjectPersonId, AccessPolicyService.ReadPermission),
        });
        await service.CreateDraftAsync(new CreateAccessPolicyRequest { PolicyKey = "main-campus", Rules = [FullRuleRequest()] }, Author, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77", ct);

        var policy = await service.GetAsync("main-campus", readOnlyActor, ct);
        var listed = await service.ListAsync(readOnlyActor, ct);

        Assert.Equal("main-campus", policy.PolicyKey);
        Assert.Single(listed);
        var error = await Assert.ThrowsAsync<AccessPolicyError>(() => service.ReviseDraftAsync(
            "main-campus", new ReviseDraftRequest { Version = policy.ConcurrencyVersion, Rules = [FullRuleRequest()] }, readOnlyActor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a79", ct));
        Assert.Equal("permission_required", error.Code);
    }

    // --- AC04: audit-atomicity ---

    [Fact]
    public async Task Creating_a_draft_writes_an_audit_event()
    {
        var ct = TestContext.Current.CancellationToken;
        var (service, _, auditSink) = BuildService();

        await service.CreateDraftAsync(new CreateAccessPolicyRequest { PolicyKey = "main-campus", Rules = [FullRuleRequest()] }, Author, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77", ct);

        var recorded = Assert.Single(auditSink.Events);
        Assert.Equal("access_policy.draft_created", recorded.Action);
        Assert.Equal("main-campus", recorded.Target.Id);
    }

    private sealed class ThrowingAuditSink : IAccessPolicyAuditSink
    {
        public Task WriteAsync(Audit.AccessPolicyAuditEvent auditEvent, CancellationToken cancellationToken = default) => throw new InvalidOperationException("simulated audit outage");

        public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }

    [Fact]
    public async Task A_confirmed_audit_sink_failure_prevents_the_draft_from_ever_being_committed()
    {
        var ct = TestContext.Current.CancellationToken;
        var repository = new InMemoryAccessPolicyRepository();
        var service = new AccessPolicyService(
            new FakeCurrentTenantProvider(TenantA), repository, new ThrowingAuditSink(), new AccessPolicyLockRegistry(),
            new FakePersonExistenceGateway(), new FakePermissionGateway());

        var error = await Assert.ThrowsAsync<AccessPolicyError>(() => service.CreateDraftAsync(
            new CreateAccessPolicyRequest { PolicyKey = "main-campus", Rules = [FullRuleRequest()] }, Author, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77", ct));

        Assert.Equal("audit_unavailable", error.Code);
        Assert.Null(await repository.GetAsync(TenantA, "main-campus", ct));
    }
}

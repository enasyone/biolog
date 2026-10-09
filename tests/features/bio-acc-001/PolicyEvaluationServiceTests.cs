using Biolog.Features.AccessPolicy.Api;
using Biolog.Features.AccessPolicy.Audit;
using Biolog.Features.AccessPolicy.Domain;
using Biolog.Features.AccessPolicy.Identifiers;
using Xunit;

namespace Biolog.Features.AccessPolicy.Tests;

/// <summary>
/// <see cref="PolicyEvaluator"/> tests are BIO-ACC-001-AC03's real proof: "deny precedence is
/// explicit" and "policy evaluation is auditable and testable" (AC04) - the pure evaluator is
/// tested directly, independent of any service/gateway wiring, then
/// <see cref="PolicyEvaluationService"/>'s own permission-gating and audit-atomicity are tested
/// separately.
/// </summary>
public sealed class PolicyEvaluationServiceTests
{
    private const string TenantA = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77";
    private const string PersonId = "per_0199e0c1-9d40-7e52-b103-7c9a4e2f6b1d";
    private const string ActorId = "per_0199e0c1-9d40-7e52-b103-7c9a4e2f6b1e";
    private static readonly AuditActor Actor = new(ActorType.User, ActorId);
    private static readonly DateTime MondayNoonUtc = new(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc); // a Monday

    private static PolicyRule Rule(int ruleId, PolicyRuleEffect effect, AssuranceLevel requiredAssurance = AssuranceLevel.Basic) => new()
    {
        RuleId = ruleId,
        Effect = effect,
        Subject = SubjectMatch.Any,
        Credential = CredentialClassMatch.Any,
        Location = LocationMatch.Any,
        Schedule = ScheduleMatch.Always,
        IdentityStatus = IdentityStatusMatch.Any,
        CardStatus = CardStatusMatch.Any,
        RequiredAssuranceLevel = requiredAssurance,
    };

    private static PolicyEvaluationContext Context(AssuranceLevel presented = AssuranceLevel.Basic) => new()
    {
        PersonId = PersonId, CredentialClass = "staff-card", LocationId = "main-gate", OccurredAtUtc = MondayNoonUtc,
        PresentedCardStatus = CardStatus.Active, PresentedAssuranceLevel = presented,
    };

    private static AccessPolicyVersionContent PublishedVersion(params PolicyRule[] rules)
    {
        var policy = Domain.AccessPolicy.Draft(TenantA, "main-campus", rules, "per_0199e0c1-9d40-7e52-b103-7c9a4e2f6b1f", UtcInstant.Now());
        var published = policy.Publish("per_0199e0c1-9d40-7e52-b103-7c9a4e2f6b10", policy.ConcurrencyVersion, UtcInstant.Now());
        return published.Latest;
    }

    // --- AC03: deny precedence is explicit ---

    [Fact]
    public void A_matching_deny_rule_wins_over_a_matching_allow_rule_regardless_of_list_order()
    {
        var version = PublishedVersion(Rule(1, PolicyRuleEffect.Allow), Rule(2, PolicyRuleEffect.Deny));

        var result = PolicyEvaluator.Evaluate("main-campus", version, Context(), IdentityStatus.Active);

        Assert.Equal(PolicyEvaluationDecision.Deny, result.Decision);
        Assert.Equal("deny_rule_matched", result.ReasonCode);
        Assert.Equal(2, result.MatchedRuleId);
    }

    [Fact]
    public void A_matching_deny_rule_wins_even_when_it_appears_before_the_allow_rule()
    {
        var version = PublishedVersion(Rule(1, PolicyRuleEffect.Deny), Rule(2, PolicyRuleEffect.Allow));

        var result = PolicyEvaluator.Evaluate("main-campus", version, Context(), IdentityStatus.Active);

        Assert.Equal(PolicyEvaluationDecision.Deny, result.Decision);
        Assert.Equal(1, result.MatchedRuleId);
    }

    [Fact]
    public void No_matching_rule_at_all_defaults_to_deny()
    {
        var version = PublishedVersion(Rule(1, PolicyRuleEffect.Allow) with { Location = new LocationMatch(LocationMatchKind.SpecificLocation, "some-other-gate") });

        var result = PolicyEvaluator.Evaluate("main-campus", version, Context(), IdentityStatus.Active);

        Assert.Equal(PolicyEvaluationDecision.Deny, result.Decision);
        Assert.Equal("no_matching_rule", result.ReasonCode);
        Assert.Null(result.MatchedRuleId);
    }

    [Fact]
    public void A_matching_allow_rule_permits_access_when_presented_assurance_satisfies_the_requirement()
    {
        var version = PublishedVersion(Rule(1, PolicyRuleEffect.Allow, AssuranceLevel.Elevated));

        var result = PolicyEvaluator.Evaluate("main-campus", version, Context(presented: AssuranceLevel.High), IdentityStatus.Active);

        Assert.Equal(PolicyEvaluationDecision.Allow, result.Decision);
        Assert.Equal("allow_rule_matched", result.ReasonCode);
        Assert.Equal(1, result.MatchedRuleId);
    }

    [Fact]
    public void A_matching_allow_rule_denies_with_a_distinct_reason_when_presented_assurance_is_insufficient()
    {
        var version = PublishedVersion(Rule(1, PolicyRuleEffect.Allow, AssuranceLevel.High));

        var result = PolicyEvaluator.Evaluate("main-campus", version, Context(presented: AssuranceLevel.Basic), IdentityStatus.Active);

        Assert.Equal(PolicyEvaluationDecision.Deny, result.Decision);
        Assert.Equal("insufficient_assurance", result.ReasonCode);
        Assert.Equal(1, result.MatchedRuleId);
    }

    [Fact]
    public void No_published_version_denies_fail_closed()
    {
        var result = PolicyEvaluator.Evaluate("main-campus", publishedVersion: null, Context(), IdentityStatus.Active);

        Assert.Equal(PolicyEvaluationDecision.Deny, result.Decision);
        Assert.Equal("no_published_policy", result.ReasonCode);
    }

    [Fact]
    public void An_unresolvable_identity_status_denies_fail_closed_even_when_a_rule_would_otherwise_match()
    {
        var version = PublishedVersion(Rule(1, PolicyRuleEffect.Allow));

        var result = PolicyEvaluator.Evaluate("main-campus", version, Context(), identityStatus: null);

        Assert.Equal(PolicyEvaluationDecision.Deny, result.Decision);
        Assert.Equal("identity_status_unresolved", result.ReasonCode);
    }

    [Fact]
    public void A_rule_requiring_a_different_identity_status_does_not_match_a_suspended_subject()
    {
        var version = PublishedVersion(Rule(1, PolicyRuleEffect.Allow) with { IdentityStatus = new IdentityStatusMatch(false, new HashSet<IdentityStatus> { IdentityStatus.Active }) });

        var result = PolicyEvaluator.Evaluate("main-campus", version, Context(), IdentityStatus.Suspended);

        Assert.Equal(PolicyEvaluationDecision.Deny, result.Decision);
        Assert.Equal("no_matching_rule", result.ReasonCode);
    }

    [Fact]
    public void A_schedule_window_that_does_not_cover_the_requested_time_does_not_match()
    {
        var outsideWindow = new ScheduleMatch(ScheduleMatchKind.Windows, [new ScheduleWindow(DayOfWeek.Monday, new TimeOnly(8, 0), new TimeOnly(9, 0))]);
        var version = PublishedVersion(Rule(1, PolicyRuleEffect.Allow) with { Schedule = outsideWindow });

        var result = PolicyEvaluator.Evaluate("main-campus", version, Context(), IdentityStatus.Active); // MondayNoonUtc is 12:00, outside 08:00-09:00

        Assert.Equal(PolicyEvaluationDecision.Deny, result.Decision);
        Assert.Equal("no_matching_rule", result.ReasonCode);
    }

    // --- PolicyEvaluationService: RBAC integration and audit-atomicity ---

    private static (PolicyEvaluationService Service, InMemoryAccessPolicyAuditSink AuditSink) BuildService(
        AccessPolicyVersionContent? publishedVersion, IdentityStatus? identityStatus, IReadOnlySet<(string, string)>? grantedPermissions = null)
    {
        var repository = new InMemoryAccessPolicyRepository();
        if (publishedVersion is not null)
        {
            var policy = Domain.AccessPolicy.Draft(TenantA, "main-campus", publishedVersion.Rules, "per_0199e0c1-9d40-7e52-b103-7c9a4e2f6b1f", UtcInstant.Now());
            var created = repository.CreateAsync(policy, CancellationToken.None).GetAwaiter().GetResult();
            var published = created.Publish("per_0199e0c1-9d40-7e52-b103-7c9a4e2f6b10", created.ConcurrencyVersion, UtcInstant.Now());
            repository.ReplaceAsync(created, published, CancellationToken.None).GetAwaiter().GetResult();
        }

        var auditSink = new InMemoryAccessPolicyAuditSink();
        var identityStatuses = identityStatus is IdentityStatus s
            ? new Dictionary<string, IdentityStatus> { [PersonId] = s }
            : new Dictionary<string, IdentityStatus>();
        var service = new PolicyEvaluationService(
            new FakeCurrentTenantProvider(TenantA), repository, new FakeIdentityStatusGateway(identityStatuses),
            new FakePermissionGateway(grantedPermissions), auditSink);
        return (service, auditSink);
    }

    private static EvaluateAccessPolicyRequest ValidRequest() => new()
    {
        PersonId = PersonId, CredentialClass = "staff-card", LocationId = "main-gate", PresentedCardStatus = "active", PresentedAssuranceLevel = "BASIC",
    };

    [Fact]
    public async Task Evaluating_without_the_evaluate_permission_is_rejected()
    {
        var ct = TestContext.Current.CancellationToken;
        var (service, _) = BuildService(PublishedVersion(Rule(1, PolicyRuleEffect.Allow)), IdentityStatus.Active, grantedPermissions: new HashSet<(string, string)>());

        var error = await Assert.ThrowsAsync<AccessPolicyError>(() => service.EvaluateAsync(
            "main-campus", ValidRequest(), Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77", ct));

        Assert.Equal("permission_required", error.Code);
    }

    [Fact]
    public async Task Evaluating_writes_an_audit_event_whose_outcome_matches_the_decision()
    {
        var ct = TestContext.Current.CancellationToken;
        var (service, auditSink) = BuildService(PublishedVersion(Rule(1, PolicyRuleEffect.Deny)), IdentityStatus.Active);

        var result = await service.EvaluateAsync("main-campus", ValidRequest(), Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77", ct);

        Assert.Equal(PolicyEvaluationDecision.Deny, result.Decision);
        var recorded = Assert.Single(auditSink.Events);
        Assert.Equal("access_policy.evaluate", recorded.Action);
        Assert.Equal(AuditOutcome.Denied, recorded.Outcome);
        Assert.Equal("deny_rule_matched", recorded.ReasonCode);
    }

    [Fact]
    public async Task An_evaluation_request_with_a_malformed_person_id_is_rejected_as_a_validation_failure()
    {
        var ct = TestContext.Current.CancellationToken;
        var (service, _) = BuildService(PublishedVersion(Rule(1, PolicyRuleEffect.Allow)), IdentityStatus.Active);

        var error = await Assert.ThrowsAsync<AccessPolicyError>(() => service.EvaluateAsync(
            "main-campus", ValidRequest() with { PersonId = "not-a-canonical-id" }, Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77", ct));

        Assert.Equal("validation_failed", error.Code);
    }
}

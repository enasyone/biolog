using Biolog.Features.AccessPolicy.Domain;
using Biolog.Features.AccessPolicy.Identifiers;
using Xunit;

namespace Biolog.Features.AccessPolicy.Tests;

/// <summary>
/// Applies BIO-IDN-001-QA-01's lesson from the start, rather than waiting for a review round to
/// find the same class of gap here too: every persistence boundary in this project must
/// independently refuse an illegal initial state and a stale/foreign-tenant write, and every
/// aggregate/repository/service must be inaccessible to ordinary production consumers. This test
/// project has <c>InternalsVisibleTo</c> access specifically so these attacks can be driven
/// directly.
/// </summary>
public sealed class AccessPolicyGovernanceTests
{
    private const string TenantA = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77";
    private const string TenantB = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a88";

    private static PolicyRule SimpleRule() => new()
    {
        RuleId = 1, Effect = PolicyRuleEffect.Allow, Subject = SubjectMatch.Any, Credential = CredentialClassMatch.Any,
        Location = LocationMatch.Any, Schedule = ScheduleMatch.Always, IdentityStatus = IdentityStatusMatch.Any, CardStatus = CardStatusMatch.Any,
        RequiredAssuranceLevel = AssuranceLevel.Basic,
    };

    private static Domain.AccessPolicy NewPolicy(string tenantId, string policyKey = "main-campus") =>
        Domain.AccessPolicy.Draft(tenantId, policyKey, [SimpleRule()], "per_0199e0c1-9d40-7e52-b103-7c9a4e2f6b1d", UtcInstant.Now());

    [Fact]
    public async Task CreateAsync_rejects_an_initial_state_that_is_not_a_single_draft_version_at_concurrency_1()
    {
        var ct = TestContext.Current.CancellationToken;
        var repository = new InMemoryAccessPolicyRepository();
        var policy = NewPolicy(TenantA);
        var published = policy.Publish("per_0199e0c1-9d40-7e52-b103-7c9a4e2f6b1e", policy.ConcurrencyVersion, UtcInstant.Now());

        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.CreateAsync(published, ct));
    }

    [Fact]
    public async Task ReplaceAsync_rejects_moving_to_a_foreign_tenant()
    {
        var ct = TestContext.Current.CancellationToken;
        var repository = new InMemoryAccessPolicyRepository();
        var original = await repository.CreateAsync(NewPolicy(TenantA), ct);
        var forged = Domain.AccessPolicy.Draft(TenantB, original.PolicyKey, [SimpleRule()], "per_0199e0c1-9d40-7e52-b103-7c9a4e2f6b1e", UtcInstant.Now());

        var error = await Assert.ThrowsAsync<AccessPolicyError>(() => repository.ReplaceAsync(original, forged, ct));

        Assert.Equal("identity_immutable", error.Code);
        Assert.NotNull(await repository.GetAsync(TenantA, original.PolicyKey, ct));
        Assert.Null(await repository.GetAsync(TenantB, original.PolicyKey, ct));
    }

    [Fact]
    public async Task ReplaceAsync_rejects_a_stale_expected_version()
    {
        var ct = TestContext.Current.CancellationToken;
        var repository = new InMemoryAccessPolicyRepository();
        var original = await repository.CreateAsync(NewPolicy(TenantA), ct);
        var revised = original.ReviseDraft([SimpleRule()], "per_0199e0c1-9d40-7e52-b103-7c9a4e2f6b1e", original.ConcurrencyVersion, UtcInstant.Now());
        await repository.ReplaceAsync(original, revised, ct);

        var staleRevision = original.ReviseDraft([SimpleRule()], "per_0199e0c1-9d40-7e52-b103-7c9a4e2f6b1e", original.ConcurrencyVersion, UtcInstant.Now());
        var error = await Assert.ThrowsAsync<AccessPolicyError>(() => repository.ReplaceAsync(original, staleRevision, ct));

        Assert.Equal("concurrency_conflict", error.Code);
    }

    [Fact]
    public async Task CreateAsync_rejects_a_second_policy_with_an_already_used_key_in_the_same_tenant()
    {
        var ct = TestContext.Current.CancellationToken;
        var repository = new InMemoryAccessPolicyRepository();
        await repository.CreateAsync(NewPolicy(TenantA), ct);

        var error = await Assert.ThrowsAsync<AccessPolicyError>(() => repository.CreateAsync(NewPolicy(TenantA), ct));

        Assert.Equal("policy_already_exists", error.Code);
    }

    [Fact]
    public void IAccessPolicyRepository_and_its_implementation_are_not_public()
    {
        Assert.False(typeof(IAccessPolicyRepository).IsPublic);
        Assert.False(typeof(InMemoryAccessPolicyRepository).IsPublic);
    }

    [Fact]
    public void AccessPolicy_has_no_publicly_accessible_constructor()
    {
        Assert.Empty(typeof(Domain.AccessPolicy).GetConstructors(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance));
    }

    [Fact]
    public void AccessPolicyVersionContent_has_no_publicly_accessible_constructor()
    {
        Assert.Empty(typeof(AccessPolicyVersionContent).GetConstructors(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance));
    }

    [Fact]
    public void AccessPolicyService_and_PolicyEvaluationService_are_both_internal()
    {
        Assert.False(typeof(AccessPolicyService).IsPublic);
        Assert.False(typeof(PolicyEvaluationService).IsPublic);
    }

    [Fact]
    public void This_project_has_no_assembly_reference_to_any_other_feature_project()
    {
        // AC01's subject-existence check, identity-status resolution and RBAC permission check
        // all go through this project's own narrow gateways (IPersonExistenceGateway,
        // IIdentityStatusGateway, IPermissionGateway), never a ProjectReference - confirmed
        // adversarially by inspecting the compiled assembly's own referenced-assembly list.
        var referenced = typeof(AccessPolicyService).Assembly.GetReferencedAssemblies().Select(a => a.Name).ToList();
        Assert.DoesNotContain(referenced, n => n is not null && n.Contains("PersonRegistry", StringComparison.Ordinal));
        Assert.DoesNotContain(referenced, n => n is not null && n.Contains("Rbac", StringComparison.Ordinal));
        Assert.DoesNotContain(referenced, n => n is not null && n.Contains("CardIssuance", StringComparison.Ordinal));
        Assert.DoesNotContain(referenced, n => n is not null && n.Contains("Biolog.Api", StringComparison.Ordinal));
    }
}

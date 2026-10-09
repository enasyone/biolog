using Biolog.Features.AccessDecisions.Api;
using Biolog.Features.AccessDecisions.Audit;
using Biolog.Features.AccessDecisions.Domain;
using Xunit;

namespace Biolog.Features.AccessDecisions.Tests;

/// <summary>AC01 (evidence vs decision), AC02 (deny precedence flows through unchanged), AC03 (policy version/reason recorded), AC04 (idempotent replay) against <see cref="AccessDecisionService"/>.</summary>
public class AccessDecisionServiceTests
{
    private const string TenantId = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77";
    private const string PersonId = "per_0199e0c2-1111-7222-8333-444455556666";
    private static readonly AuditActor Actor = new(ActorType.User, "usr-gate-operator-1");

    private sealed record Harness(
        AccessDecisionService Service, InMemoryAccessDecisionEventRepository EventRepository,
        InMemoryAccessDecisionAuditSink AuditSink, FakePolicyDecisionGateway PolicyGateway,
        FakeBiometricEvidenceGateway FingerprintGateway, FakeBiometricEvidenceGateway FaceGateway);

    private static Harness MakeHarness(
        PolicyDecisionResult? policyResponse = null,
        BiometricEvidenceEntry? fingerprintResponse = null,
        BiometricEvidenceEntry? faceResponse = null,
        IPersonExistenceGateway? personGateway = null)
    {
        var eventRepository = new InMemoryAccessDecisionEventRepository();
        var auditSink = new InMemoryAccessDecisionAuditSink();
        var response = policyResponse ?? new PolicyDecisionResult { Decision = AccessDecisionOutcome.Allow, ReasonCode = "allow_rule_matched", PolicyKey = "main-gate", PolicyVersionNumber = 1, MatchedRuleId = 1 };
        var policyGateway = new FakePolicyDecisionGateway(_ => response);
        var fingerprintGateway = new FakeBiometricEvidenceGateway(fingerprintResponse ?? new BiometricEvidenceEntry { Modality = BiometricModality.Fingerprint, ComparisonOutcome = BiometricComparisonOutcome.Matched, Score = 0.95 });
        var faceGateway = new FakeBiometricEvidenceGateway(faceResponse ?? new BiometricEvidenceEntry { Modality = BiometricModality.Face, ComparisonOutcome = BiometricComparisonOutcome.Matched, Score = 0.9 });

        var service = new AccessDecisionService(
            new FakeCurrentTenantProvider(TenantId), eventRepository, auditSink, new AccessDecisionLockRegistry(),
            personGateway ?? new FakePersonExistenceGateway(), policyGateway, fingerprintGateway, faceGateway);

        return new Harness(service, eventRepository, auditSink, policyGateway, fingerprintGateway, faceGateway);
    }

    private static DecideAccessRequest Request(string? idempotencyKey = null, bool withFingerprint = false, bool withFace = false) => new()
    {
        PersonId = PersonId,
        CredentialClass = "staff",
        LocationId = "loc_main_gate",
        PresentedCardStatus = "active",
        PolicyKey = "main-gate",
        IdempotencyKey = idempotencyKey,
        FingerprintSample = withFingerprint ? new BiometricSampleRequest { PayloadBase64 = Convert.ToBase64String([1, 2, 3]), ContentType = "application/octet-stream", CapturedAt = "2026-01-01T08:00:00.000Z" } : null,
        FaceSample = withFace ? new BiometricSampleRequest { PayloadBase64 = Convert.ToBase64String([4, 5, 6]), ContentType = "application/octet-stream", CapturedAt = "2026-01-01T08:00:00.000Z" } : null,
    };

    [Fact]
    public async Task AC01_a_card_only_request_with_no_biometric_sample_is_evaluated_as_basic_assurance()
    {
        var h = MakeHarness();

        var decision = await h.Service.DecideAsync(Request(), Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f20");

        Assert.Empty(decision.BiometricEvidence);
        Assert.Equal(AssuranceLevel.Basic, decision.PresentedAssuranceLevel);
        Assert.Equal("BASIC", h.PolicyGateway.LastQuery!.PresentedAssuranceLevel);
    }

    [Fact]
    public async Task AC01_a_matched_fingerprint_is_recorded_as_evidence_and_elevates_the_assurance_level_passed_to_the_policy_gateway()
    {
        var h = MakeHarness();

        var decision = await h.Service.DecideAsync(Request(withFingerprint: true), Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f20");

        Assert.Single(decision.BiometricEvidence);
        Assert.Equal(BiometricComparisonOutcome.Matched, decision.BiometricEvidence[0].ComparisonOutcome);
        Assert.Equal(AssuranceLevel.Elevated, decision.PresentedAssuranceLevel);
        Assert.Equal("ELEVATED", h.PolicyGateway.LastQuery!.PresentedAssuranceLevel);
    }

    [Fact]
    public async Task AC01_a_matched_biometric_never_overrides_a_policy_deny_the_gateways_own_decision_always_wins()
    {
        var deny = new PolicyDecisionResult { Decision = AccessDecisionOutcome.Deny, ReasonCode = "deny_rule_matched", PolicyKey = "main-gate", PolicyVersionNumber = 2, MatchedRuleId = 5 };
        var h = MakeHarness(policyResponse: deny);

        var decision = await h.Service.DecideAsync(Request(withFingerprint: true, withFace: true), Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f20");

        Assert.Equal(AccessDecisionOutcome.Deny, decision.Decision);
        Assert.Equal("deny_rule_matched", decision.ReasonCode);
        Assert.Equal(2, decision.BiometricEvidence.Count);
        Assert.All(decision.BiometricEvidence, e => Assert.Equal(BiometricComparisonOutcome.Matched, e.ComparisonOutcome));
    }

    [Fact]
    public async Task AC02_a_deny_reason_the_policy_gateway_reports_for_a_suspended_or_revoked_identity_flows_through_unchanged()
    {
        var deny = new PolicyDecisionResult { Decision = AccessDecisionOutcome.Deny, ReasonCode = "identity_status_unresolved", PolicyKey = "main-gate", PolicyVersionNumber = 1 };
        var h = MakeHarness(policyResponse: deny);

        var decision = await h.Service.DecideAsync(Request(), Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f20");

        Assert.Equal(AccessDecisionOutcome.Deny, decision.Decision);
        Assert.Equal("identity_status_unresolved", decision.ReasonCode);
    }

    [Fact]
    public async Task AC03_the_decision_records_the_policy_key_version_and_matched_rule()
    {
        var allow = new PolicyDecisionResult { Decision = AccessDecisionOutcome.Allow, ReasonCode = "allow_rule_matched", PolicyKey = "main-gate", PolicyVersionNumber = 7, MatchedRuleId = 3 };
        var h = MakeHarness(policyResponse: allow);

        var decision = await h.Service.DecideAsync(Request(), Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f20");

        Assert.Equal("main-gate", decision.PolicyKey);
        Assert.Equal(7, decision.PolicyVersionNumber);
        Assert.Equal(3, decision.MatchedRuleId);
        Assert.Single(h.AuditSink.All());
    }

    [Fact]
    public async Task AC04_a_repeated_idempotency_key_returns_the_same_decision_without_recalling_the_biometric_gateways_a_second_time()
    {
        var h = MakeHarness();

        var first = await h.Service.DecideAsync(Request("idem-1", withFingerprint: true), Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f20");
        var second = await h.Service.DecideAsync(Request("idem-1", withFingerprint: true), Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f21");

        Assert.Equal(first.EventId, second.EventId);
        Assert.Equal(1, h.FingerprintGateway.CallCount);
        Assert.Equal(1, h.PolicyGateway.CallCount);
    }

    [Fact]
    public async Task a_person_that_does_not_exist_in_the_resolved_tenant_is_rejected()
    {
        var h = MakeHarness(personGateway: new FakePersonExistenceGateway(new HashSet<string>()));

        var error = await Assert.ThrowsAsync<AccessDecisionError>(() => h.Service.DecideAsync(Request(), Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f20"));

        Assert.Equal("person_not_found", error.Code);
        Assert.Equal(0, h.PolicyGateway.CallCount);
    }

    [Fact]
    public async Task a_request_missing_required_fields_fails_validation_before_any_gateway_is_called()
    {
        var h = MakeHarness();
        var invalid = Request() with { CredentialClass = null, LocationId = null, PolicyKey = null };

        var error = await Assert.ThrowsAsync<AccessDecisionError>(() => h.Service.DecideAsync(invalid, Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f20"));

        Assert.Equal("validation_failed", error.Code);
        Assert.Equal(0, h.PolicyGateway.CallCount);
    }
}

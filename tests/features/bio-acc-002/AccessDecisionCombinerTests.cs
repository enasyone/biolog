using Biolog.Features.AccessDecisions.Domain;
using Xunit;

namespace Biolog.Features.AccessDecisions.Tests;

public sealed class AccessDecisionCombinerTests
{
    private static BiometricEvidenceEntry Entry(BiometricModality modality, BiometricComparisonOutcome outcome, double? score = null) =>
        new() { Modality = modality, ComparisonOutcome = outcome, Score = score };

    [Fact]
    public void AC01_no_evidence_derives_basic_assurance()
    {
        var level = AccessDecisionCombiner.DeriveAssuranceLevel([]);
        Assert.Equal(AssuranceLevel.Basic, level);
    }

    [Fact]
    public void AC01_a_single_matched_modality_derives_elevated_assurance()
    {
        var level = AccessDecisionCombiner.DeriveAssuranceLevel([Entry(BiometricModality.Fingerprint, BiometricComparisonOutcome.Matched, 0.95)]);
        Assert.Equal(AssuranceLevel.Elevated, level);
    }

    [Fact]
    public void AC01_two_independently_matched_modalities_derive_high_assurance()
    {
        var level = AccessDecisionCombiner.DeriveAssuranceLevel([
            Entry(BiometricModality.Fingerprint, BiometricComparisonOutcome.Matched, 0.95),
            Entry(BiometricModality.Face, BiometricComparisonOutcome.Matched, 0.91),
        ]);
        Assert.Equal(AssuranceLevel.High, level);
    }

    [Theory]
    [InlineData(BiometricComparisonOutcome.NotMatched)]
    [InlineData(BiometricComparisonOutcome.AcquisitionFailed)]
    [InlineData(BiometricComparisonOutcome.ReferenceStale)]
    [InlineData(BiometricComparisonOutcome.NotEnrolled)]
    public void AC01_a_failed_or_missing_biometric_attempt_never_elevates_assurance_on_its_own(BiometricComparisonOutcome outcome)
    {
        var level = AccessDecisionCombiner.DeriveAssuranceLevel([Entry(BiometricModality.Fingerprint, outcome)]);
        Assert.Equal(AssuranceLevel.Basic, level);
    }

    [Fact]
    public void AC01_evidence_is_never_itself_the_allow_deny_decision()
    {
        // A MATCHED fingerprint (the strongest possible evidence) still produces whatever the
        // policy gateway decided - this test proves Combine never overrides it, even when every
        // biometric signal present says "matched".
        var evidence = new[] { Entry(BiometricModality.Fingerprint, BiometricComparisonOutcome.Matched, 0.99) };
        var policyResult = new PolicyDecisionResult { Decision = AccessDecisionOutcome.Deny, ReasonCode = "deny_rule_matched", PolicyKey = "pol", PolicyVersionNumber = 3, MatchedRuleId = 2 };

        var decision = AccessDecisionCombiner.Combine(
            "evt_1", "ten_1", "per_1", null, "staff", "loc_1", CardStatus.Active, evidence, policyResult, "2026-01-01T00:00:00.000Z", "2026-01-01T00:00:01.000Z", "evt_2", null);

        Assert.Equal(AccessDecisionOutcome.Deny, decision.Decision);
        Assert.Equal("deny_rule_matched", decision.ReasonCode);
        Assert.Same(evidence, decision.BiometricEvidence);
    }

    [Fact]
    public void AC04_the_same_inputs_replayed_produce_a_structurally_identical_decision()
    {
        var evidence = new[] { Entry(BiometricModality.Face, BiometricComparisonOutcome.Matched, 0.87) };
        var policyResult = new PolicyDecisionResult { Decision = AccessDecisionOutcome.Allow, ReasonCode = "allow_rule_matched", PolicyKey = "pol", PolicyVersionNumber = 1, MatchedRuleId = 1 };

        var first = AccessDecisionCombiner.Combine(
            "evt_fixed", "ten_1", "per_1", "profile_a", "staff", "loc_1", CardStatus.Active, evidence, policyResult, "2026-01-01T00:00:00.000Z", "2026-01-01T00:00:01.000Z", "evt_corr", "idem_1");
        var second = AccessDecisionCombiner.Combine(
            "evt_fixed", "ten_1", "per_1", "profile_a", "staff", "loc_1", CardStatus.Active, evidence, policyResult, "2026-01-01T00:00:00.000Z", "2026-01-01T00:00:01.000Z", "evt_corr", "idem_1");

        Assert.Equal(first.EventId, second.EventId);
        Assert.Equal(first.Decision, second.Decision);
        Assert.Equal(first.ReasonCode, second.ReasonCode);
        Assert.Equal(first.PresentedAssuranceLevel, second.PresentedAssuranceLevel);
        Assert.Equal(first.PolicyVersionNumber, second.PolicyVersionNumber);
        Assert.Equal(first.MatchedRuleId, second.MatchedRuleId);
        Assert.Equal(first.BiometricEvidence.Count, second.BiometricEvidence.Count);
        Assert.Equal(first.BiometricEvidence[0], second.BiometricEvidence[0]);
        Assert.Equal(
            AccessDecisionCombiner.ComputeFingerprint(first.TenantId, first.PersonId, first.ProfileKey, first.CredentialClass, first.LocationId, first.PresentedCardStatus, first.BiometricEvidence, first.PolicyKey, first.OccurredAt),
            AccessDecisionCombiner.ComputeFingerprint(second.TenantId, second.PersonId, second.ProfileKey, second.CredentialClass, second.LocationId, second.PresentedCardStatus, second.BiometricEvidence, second.PolicyKey, second.OccurredAt));
    }

    [Fact]
    public void AC04_a_different_evidence_score_changes_the_fingerprint()
    {
        var a = AccessDecisionCombiner.ComputeFingerprint("ten_1", "per_1", null, "staff", "loc_1", CardStatus.Active, [Entry(BiometricModality.Face, BiometricComparisonOutcome.Matched, 0.80)], "pol", "2026-01-01T00:00:00.000Z");
        var b = AccessDecisionCombiner.ComputeFingerprint("ten_1", "per_1", null, "staff", "loc_1", CardStatus.Active, [Entry(BiometricModality.Face, BiometricComparisonOutcome.Matched, 0.81)], "pol", "2026-01-01T00:00:00.000Z");
        Assert.NotEqual(a, b);
    }
}

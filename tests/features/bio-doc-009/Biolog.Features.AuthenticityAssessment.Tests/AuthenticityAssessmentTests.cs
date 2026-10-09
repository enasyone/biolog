using Biolog.Features.AuthenticityAssessment.Audit;
using Biolog.Features.AuthenticityAssessment.Model;
using Biolog.Features.AuthenticityAssessment.Tests.Fixtures;
using Xunit;

namespace Biolog.Features.AuthenticityAssessment.Tests;

/// <summary>Developer tests for BIO-DOC-009-AC05 ("manual review can override workflow disposition
/// but cannot rewrite raw evidence").</summary>
public sealed class AuthenticityAssessmentTests
{
    private const string ActorId = "actor-42";
    private const string ReviewerId = "reviewer-7";
    private const string ValidTimestamp = "2026-09-23T12:00:00.000Z";
    private const string CorrelationId = "corr-1";

    private static Model.AuthenticityAssessment FromInconclusive()
    {
        var inputs = new AuthenticityAssessmentInputs
        {
            DocumentSessionId = "session-1",
            CrossComparison = CrossComparisonFixtures.NoFields(),
        };
        var result = AuthenticityAssessmentResult.Compute(inputs, ActorId, ValidTimestamp, CorrelationId, new InMemoryAuthenticityAssessmentAuditSink());
        return Model.AuthenticityAssessment.FromResult(result);
    }

    [Fact]
    public void EffectiveDisposition_matches_the_policy_result_when_no_override_is_active()
    {
        var assessment = FromInconclusive();
        Assert.Null(assessment.Override);
        Assert.Equal(assessment.Result.PolicyDisposition, assessment.EffectiveDisposition);
    }

    [Fact]
    public void AC05_a_manual_override_changes_the_effective_disposition()
    {
        var assessment = FromInconclusive();
        Assert.Equal(AuthenticityDisposition.Inconclusive, assessment.EffectiveDisposition);

        assessment.ApplyManualOverride(
            AuthenticityDisposition.DocumentAuthentic, "Operator manually verified the document in person.",
            ReviewerId, ValidTimestamp, CorrelationId, expectedVersion: 1, new InMemoryAuthenticityAssessmentAuditSink());

        Assert.Equal(AuthenticityDisposition.DocumentAuthentic, assessment.EffectiveDisposition);
    }

    [Fact]
    public void AC05_a_manual_override_never_changes_the_policy_engines_own_computed_disposition()
    {
        var assessment = FromInconclusive();
        var originalPolicyDisposition = assessment.Result.PolicyDisposition;

        assessment.ApplyManualOverride(
            AuthenticityDisposition.DocumentAuthentic, "Operator override.", ReviewerId, ValidTimestamp, CorrelationId,
            expectedVersion: 1, new InMemoryAuthenticityAssessmentAuditSink());

        Assert.Equal(originalPolicyDisposition, assessment.Result.PolicyDisposition);
    }

    [Fact]
    public void AC05_a_manual_override_never_changes_the_individual_checks_the_raw_evidence()
    {
        var assessment = FromInconclusive();
        var originalResult = assessment.Result;
        var originalChecks = originalResult.Checks;

        assessment.ApplyManualOverride(
            AuthenticityDisposition.DocumentAuthentic, "Operator override.", ReviewerId, ValidTimestamp, CorrelationId,
            expectedVersion: 1, new InMemoryAuthenticityAssessmentAuditSink());

        // The strongest possible proof: the exact same Result and Checks OBJECT references, not
        // just equal-looking values - ApplyManualOverride never assigns to Result at all.
        Assert.Same(originalResult, assessment.Result);
        Assert.Same(originalChecks, assessment.Result.Checks);
    }

    [Fact]
    public void AC05_the_override_records_who_and_why()
    {
        var assessment = FromInconclusive();
        assessment.ApplyManualOverride(
            AuthenticityDisposition.DocumentNotAuthentic, "Visible tampering found on physical inspection.",
            ReviewerId, ValidTimestamp, CorrelationId, expectedVersion: 1, new InMemoryAuthenticityAssessmentAuditSink());

        Assert.NotNull(assessment.Override);
        Assert.Equal(ReviewerId, assessment.Override!.OverriddenBy);
        Assert.Equal("Visible tampering found on physical inspection.", assessment.Override.Reason);
        Assert.Equal(AuthenticityDisposition.DocumentNotAuthentic, assessment.Override.Disposition);
    }

    [Fact]
    public void AC05_the_override_is_durably_audited_before_returning()
    {
        var sink = new InMemoryAuthenticityAssessmentAuditSink();
        var assessment = FromInconclusive();
        assessment.ApplyManualOverride(
            AuthenticityDisposition.DocumentAuthentic, "Operator override.", ReviewerId, ValidTimestamp, CorrelationId,
            expectedVersion: 1, sink);

        Assert.Contains(sink.Events, e => e.Action == "authenticity_assessment.manually_overridden");
    }

    [Fact]
    public void AC05_an_override_rejects_a_blank_reason()
    {
        var assessment = FromInconclusive();
        Assert.Throws<ArgumentException>(() => assessment.ApplyManualOverride(
            AuthenticityDisposition.DocumentAuthentic, "  ", ReviewerId, ValidTimestamp, CorrelationId,
            expectedVersion: 1, new InMemoryAuthenticityAssessmentAuditSink()));
    }

    [Fact]
    public void a_stale_expected_version_on_override_is_rejected()
    {
        var assessment = FromInconclusive();
        Assert.Throws<AuthenticityAssessmentConcurrencyException>(() => assessment.ApplyManualOverride(
            AuthenticityDisposition.DocumentAuthentic, "Operator override.", ReviewerId, ValidTimestamp, CorrelationId,
            expectedVersion: 99, new InMemoryAuthenticityAssessmentAuditSink()));
    }

    [Fact]
    public void ApplyManualOverride_advances_the_aggregate_version()
    {
        var assessment = FromInconclusive();
        Assert.Equal(1, assessment.Version);

        assessment.ApplyManualOverride(
            AuthenticityDisposition.DocumentAuthentic, "Operator override.", ReviewerId, ValidTimestamp, CorrelationId,
            expectedVersion: 1, new InMemoryAuthenticityAssessmentAuditSink());

        Assert.Equal(2, assessment.Version);
    }

    [Fact]
    public void ClearOverride_reverts_the_effective_disposition_to_the_policy_engines_own_result()
    {
        var assessment = FromInconclusive();
        assessment.ApplyManualOverride(
            AuthenticityDisposition.DocumentAuthentic, "Operator override.", ReviewerId, ValidTimestamp, CorrelationId,
            expectedVersion: 1, new InMemoryAuthenticityAssessmentAuditSink());
        Assert.Equal(AuthenticityDisposition.DocumentAuthentic, assessment.EffectiveDisposition);

        assessment.ClearOverride(expectedVersion: 2);

        Assert.Null(assessment.Override);
        Assert.Equal(AuthenticityDisposition.Inconclusive, assessment.EffectiveDisposition);
    }

    [Fact]
    public void ReplaceResult_installs_a_new_result_without_clearing_an_existing_override()
    {
        var assessment = FromInconclusive();
        assessment.ApplyManualOverride(
            AuthenticityDisposition.DocumentAuthentic, "Operator override.", ReviewerId, ValidTimestamp, CorrelationId,
            expectedVersion: 1, new InMemoryAuthenticityAssessmentAuditSink());

        // A fresh, independent IVersionStore (CrossComparisonFixtures' own default) has never
        // seen "session-1" before - it expects version 1 next, not 2.
        var newInputs = new AuthenticityAssessmentInputs
        {
            DocumentSessionId = "session-1",
            CrossComparison = CrossComparisonFixtures.FieldsDisagree("session-1", versionNumber: 1),
        };
        var newResult = AuthenticityAssessmentResult.Compute(newInputs, ActorId, ValidTimestamp, "corr-2", new InMemoryAuthenticityAssessmentAuditSink());

        assessment.ReplaceResult(newResult, expectedVersion: 2);

        Assert.Same(newResult, assessment.Result);
        Assert.NotNull(assessment.Override);
        // The recompute reaches DocumentNotAuthentic on its own; the override still wins for the
        // EFFECTIVE disposition until a caller explicitly clears it.
        Assert.Equal(AuthenticityDisposition.DocumentNotAuthentic, assessment.Result.PolicyDisposition);
        Assert.Equal(AuthenticityDisposition.DocumentAuthentic, assessment.EffectiveDisposition);
    }

    [Fact]
    public void ReplaceResult_rejects_a_result_for_a_different_document_session()
    {
        var assessment = FromInconclusive();
        var mismatchedInputs = new AuthenticityAssessmentInputs
        {
            DocumentSessionId = "a-different-session",
            CrossComparison = CrossComparisonFixtures.NoFields(documentSessionId: "a-different-session"),
        };
        var mismatchedResult = AuthenticityAssessmentResult.Compute(mismatchedInputs, ActorId, ValidTimestamp, CorrelationId, new InMemoryAuthenticityAssessmentAuditSink());

        Assert.Throws<ArgumentException>(() => assessment.ReplaceResult(mismatchedResult, expectedVersion: 1));
    }

    [Fact]
    public void a_stale_expected_version_on_ReplaceResult_is_rejected()
    {
        var assessment = FromInconclusive();
        var newInputs = new AuthenticityAssessmentInputs
        {
            DocumentSessionId = "session-1",
            CrossComparison = CrossComparisonFixtures.NoFields(versionNumber: 1),
        };
        var newResult = AuthenticityAssessmentResult.Compute(newInputs, ActorId, ValidTimestamp, "corr-2", new InMemoryAuthenticityAssessmentAuditSink());

        Assert.Throws<AuthenticityAssessmentConcurrencyException>(() => assessment.ReplaceResult(newResult, expectedVersion: 99));
    }
}

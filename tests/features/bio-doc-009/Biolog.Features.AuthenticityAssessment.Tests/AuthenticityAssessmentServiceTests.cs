using Biolog.Features.AuthenticityAssessment.Api;
using Biolog.Features.AuthenticityAssessment.Audit;
using Biolog.Features.AuthenticityAssessment.Model;
using Xunit;

namespace Biolog.Features.AuthenticityAssessment.Tests;

/// <summary>
/// Direct, non-HTTP coverage for <see cref="AuthenticityAssessmentService"/> - since Zainab's
/// BIO-DOC-009-F01 security review (see <see cref="AuthenticityEndpoints"/>'s own remarks) removed
/// the unauthenticated <c>GET</c>/override HTTP routes this service used to back, <see cref="AuthenticityAssessmentService.Get"/>
/// and <see cref="AuthenticityAssessmentService.ApplyOverride"/> are no longer exercised by any
/// HTTP-level test. These tests prove the service layer itself still works correctly, so the seam
/// stays trustworthy for whichever later story maps a real, authorized endpoint onto it.
/// </summary>
public sealed class AuthenticityAssessmentServiceTests
{
    private static AuthenticityAssessmentService NewService() =>
        new(new InMemoryAuthenticityAssessmentStore(), new InMemoryAuthenticityAssessmentAuditSink());

    [Fact]
    public void ComputeNextDemoAssessment_cycles_deterministically_through_the_three_named_demo_sessions()
    {
        var service = NewService();

        var first = service.ComputeNextDemoAssessment("actor-1", "corr-1");
        var second = service.ComputeNextDemoAssessment("actor-1", "corr-2");
        var third = service.ComputeNextDemoAssessment("actor-1", "corr-3");
        var fourth = service.ComputeNextDemoAssessment("actor-1", "corr-4");

        Assert.Equal("demo-session-fully-verified", first.DocumentSessionId);
        Assert.Equal("demo-session-barcode-only", second.DocumentSessionId);
        Assert.Equal("demo-session-conflict-detected", third.DocumentSessionId);
        Assert.Equal("demo-session-fully-verified", fourth.DocumentSessionId);
        Assert.Equal(AuthenticityDisposition.DocumentAuthentic, first.Result.PolicyDisposition);
        Assert.Equal(AuthenticityDisposition.Inconclusive, second.Result.PolicyDisposition);
        Assert.Equal(AuthenticityDisposition.DocumentNotAuthentic, third.Result.PolicyDisposition);
    }

    [Fact]
    public void ComputeNextDemoAssessment_advances_the_same_sessions_own_evidence_version_on_repeat_cycles()
    {
        var service = NewService();

        var firstPass = service.ComputeNextDemoAssessment("actor-1", "corr-1");
        var firstPassEvidenceVersion = firstPass.Result.EvidenceVersion; // captured before the object is mutated further
        var firstPassSessionId = firstPass.DocumentSessionId;
        service.ComputeNextDemoAssessment("actor-1", "corr-2");
        service.ComputeNextDemoAssessment("actor-1", "corr-3");
        var secondPass = service.ComputeNextDemoAssessment("actor-1", "corr-4");

        Assert.Equal(firstPassSessionId, secondPass.DocumentSessionId);
        Assert.Equal(1, firstPassEvidenceVersion);
        Assert.Equal(2, secondPass.Result.EvidenceVersion);
        // Same aggregate object, updated in place via ReplaceResult - not a second, parallel one.
        Assert.Same(firstPass, secondPass);
    }

    [Fact]
    public void Get_returns_null_for_a_session_that_has_never_been_computed()
    {
        var service = NewService();
        Assert.Null(service.Get("never-computed-session"));
    }

    [Fact]
    public void Get_returns_the_same_aggregate_ComputeNextDemoAssessment_produced()
    {
        var service = NewService();
        var computed = service.ComputeNextDemoAssessment("actor-1", "corr-1");

        var fetched = service.Get(computed.DocumentSessionId);

        Assert.Same(computed, fetched);
    }

    [Fact]
    public void ApplyOverride_changes_the_effective_disposition_without_touching_the_raw_result()
    {
        var service = NewService();
        var computed = service.ComputeNextDemoAssessment("actor-1", "corr-1"); // demo-session-fully-verified, version 1
        var originalResult = computed.Result;

        var overridden = service.ApplyOverride(
            computed.DocumentSessionId, AuthenticityDisposition.DocumentNotAuthentic,
            "Manual inspection found tampering.", "reviewer-9", "corr-2", expectedVersion: 1);

        Assert.Same(originalResult, overridden.Result);
        Assert.Equal(AuthenticityDisposition.DocumentNotAuthentic, overridden.EffectiveDisposition);
        Assert.Equal(AuthenticityDisposition.DocumentAuthentic, overridden.Result.PolicyDisposition);
        Assert.Equal("reviewer-9", overridden.Override!.OverriddenBy);
    }

    [Fact]
    public void ApplyOverride_on_a_session_with_no_assessment_yet_throws_AssessmentNotFound()
    {
        var service = NewService();
        var error = Assert.Throws<AuthenticityAssessmentError>(() => service.ApplyOverride(
            "never-computed-session", AuthenticityDisposition.DocumentAuthentic, "reason", "actor-1", "corr-1", expectedVersion: 1));
        Assert.Equal("assessment_not_found", error.Code);
    }

    [Fact]
    public void ApplyOverride_with_a_stale_expected_version_throws_a_ConcurrencyConflict_problem_not_the_raw_domain_exception()
    {
        var service = NewService();
        var computed = service.ComputeNextDemoAssessment("actor-1", "corr-1");

        var error = Assert.Throws<AuthenticityAssessmentError>(() => service.ApplyOverride(
            computed.DocumentSessionId, AuthenticityDisposition.DocumentAuthentic, "reason", "actor-1", "corr-2", expectedVersion: 99));
        Assert.Equal("concurrency_conflict", error.Code);
    }
}

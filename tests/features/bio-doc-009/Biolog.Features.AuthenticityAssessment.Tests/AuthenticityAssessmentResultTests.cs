using Biolog.Features.AuthenticityAssessment.Audit;
using Biolog.Features.AuthenticityAssessment.Model;
using Biolog.Features.AuthenticityAssessment.Tests.Fixtures;
using Xunit;

namespace Biolog.Features.AuthenticityAssessment.Tests;

/// <summary>Developer tests for BIO-DOC-009-AC04 ("evidence, template and ruleset versions are
/// recorded") and the audit-before-commit discipline <see cref="AuthenticityAssessmentResult.Compute"/>
/// mirrors from BIO-DOC-008's own <c>CrossComparisonResult.Compute</c>.</summary>
public sealed class AuthenticityAssessmentResultTests
{
    private const string ActorId = "actor-42";
    private const string ValidTimestamp = "2026-09-23T12:00:00.000Z";
    private const string CorrelationId = "corr-1";

    private static AuthenticityAssessmentInputs Inputs(string documentSessionId = "session-1", int crossComparisonVersion = 1) => new()
    {
        DocumentSessionId = documentSessionId,
        MrzOutcome = AuthenticityCheckOutcome.Passed,
        MrzDetail = "MRZ read and check digits valid.",
        BarcodeOutcome = AuthenticityCheckOutcome.Passed,
        BarcodeDetail = "Barcode decoded and structurally valid.",
        CrossComparison = CrossComparisonFixtures.AllFieldsAgree(documentSessionId, crossComparisonVersion),
    };

    [Fact]
    public void AC04_evidence_version_is_the_underlying_cross_comparison_version_number()
    {
        var inputs = Inputs(crossComparisonVersion: 1);
        var result = AuthenticityAssessmentResult.Compute(inputs, ActorId, ValidTimestamp, CorrelationId, new InMemoryAuthenticityAssessmentAuditSink());

        Assert.Equal(inputs.CrossComparison.VersionNumber, result.EvidenceVersion);
    }

    [Fact]
    public void AC04_ruleset_version_is_recorded_as_the_policy_engines_own_constant()
    {
        var result = AuthenticityAssessmentResult.Compute(Inputs(), ActorId, ValidTimestamp, CorrelationId, new InMemoryAuthenticityAssessmentAuditSink());
        Assert.Equal(AuthenticityPolicyEngine.RulesetVersion, result.RulesetVersion);
    }

    [Fact]
    public void AC04_template_id_and_version_are_null_when_classification_was_not_performed()
    {
        var result = AuthenticityAssessmentResult.Compute(Inputs(), ActorId, ValidTimestamp, CorrelationId, new InMemoryAuthenticityAssessmentAuditSink());

        Assert.Null(result.TemplateId);
        Assert.Null(result.TemplateVersionNumber);
    }

    [Fact]
    public void carries_actor_and_timestamp_attribution_exactly_as_supplied()
    {
        var result = AuthenticityAssessmentResult.Compute(Inputs(), ActorId, ValidTimestamp, CorrelationId, new InMemoryAuthenticityAssessmentAuditSink());

        Assert.Equal("session-1", result.DocumentSessionId);
        Assert.Equal(ActorId, result.ComputedByActorId);
        Assert.Equal(ValidTimestamp, result.ComputedAt);
    }

    [Fact]
    public void a_durable_audit_event_is_written_before_the_result_is_returned()
    {
        var sink = new InMemoryAuthenticityAssessmentAuditSink();
        var result = AuthenticityAssessmentResult.Compute(Inputs(), ActorId, ValidTimestamp, CorrelationId, sink);

        Assert.Contains(sink.Events, e => e.EventId == result.AuditEventId);
        Assert.Equal("authenticity_assessment.computed", sink.Events.Single().Action);
    }

    [Fact]
    public void a_genuinely_unavailable_audit_sink_throws_rather_than_returning_a_successful_result()
    {
        var sink = new AlwaysFailingAuditSink();
        Assert.Throws<AuthenticityAssessmentAuditUnavailableException>(
            () => AuthenticityAssessmentResult.Compute(Inputs(), ActorId, ValidTimestamp, CorrelationId, sink));
    }

    [Fact]
    public void an_ambiguous_write_that_actually_persisted_is_resolved_via_ExistsAsync_not_treated_as_a_failure()
    {
        var sink = new ThrowsOnWriteButPersistsAuditSink();
        var result = AuthenticityAssessmentResult.Compute(Inputs(), ActorId, ValidTimestamp, CorrelationId, sink);

        Assert.NotNull(result);
        Assert.NotEmpty(result.AuditEventId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("2026-09-23")]
    [InlineData("2026-09-23 12:00:00")]
    public void computedAt_must_be_a_full_RFC_3339_date_time(string invalidTimestamp)
    {
        Assert.Throws<ArgumentException>(
            () => AuthenticityAssessmentResult.Compute(Inputs(), ActorId, invalidTimestamp, CorrelationId, new InMemoryAuthenticityAssessmentAuditSink()));
    }

    [Fact]
    public void a_blank_actor_id_is_rejected()
    {
        Assert.Throws<ArgumentException>(
            () => AuthenticityAssessmentResult.Compute(Inputs(), "  ", ValidTimestamp, CorrelationId, new InMemoryAuthenticityAssessmentAuditSink()));
    }

    [Fact]
    public void a_blank_correlation_id_is_rejected()
    {
        Assert.Throws<ArgumentException>(
            () => AuthenticityAssessmentResult.Compute(Inputs(), ActorId, ValidTimestamp, "  ", new InMemoryAuthenticityAssessmentAuditSink()));
    }

    private sealed class AlwaysFailingAuditSink : IAuthenticityAssessmentAuditSink
    {
        public Task WriteAsync(AuthenticityAssessmentAuditEvent auditEvent, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("simulated sink outage");

        public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }

    private sealed class ThrowsOnWriteButPersistsAuditSink : IAuthenticityAssessmentAuditSink
    {
        private readonly HashSet<string> _persisted = [];

        public Task WriteAsync(AuthenticityAssessmentAuditEvent auditEvent, CancellationToken cancellationToken = default)
        {
            _persisted.Add(auditEvent.EventId);
            throw new InvalidOperationException("simulated ack failure after a durable commit");
        }

        public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default) => Task.FromResult(_persisted.Contains(eventId));
    }
}

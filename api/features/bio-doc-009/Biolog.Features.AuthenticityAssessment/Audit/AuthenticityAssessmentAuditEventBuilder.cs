using Biolog.Features.AuthenticityAssessment.Model;

namespace Biolog.Features.AuthenticityAssessment.Audit;

/// <summary>The only sanctioned way to build an <see cref="AuthenticityAssessmentAuditEvent"/> -
/// mirrors <c>EvidenceComparisonAuditEventBuilder</c>'s shape, scoped to this feature's two
/// actions (compute, manual override - AC05).</summary>
public static class AuthenticityAssessmentAuditEventBuilder
{
    public static AuthenticityAssessmentAuditEvent BuildComputed(
        AuditActor actor,
        string documentSessionId,
        string correlationId,
        AuthenticityDisposition disposition,
        IReadOnlyList<AuthenticityCheckResult> checks,
        int evidenceVersion,
        string? templateId,
        int? templateVersionNumber,
        string rulesetVersion)
    {
        var now = AuthenticityAssessmentClock.Now();
        return new AuthenticityAssessmentAuditEvent
        {
            EventId = AuthenticityAssessmentEventId.New(),
            TenantId = null, // no tenant dependency is declared by this story
            Actor = actor,
            Device = null,
            Action = "authenticity_assessment.computed",
            Target = new AuditTarget("document_session", documentSessionId),
            Outcome = AuditOutcome.Allowed,
            ReasonCode = disposition == AuthenticityDisposition.DocumentAuthentic
                ? "assessment_authentic"
                : disposition == AuthenticityDisposition.DocumentNotAuthentic
                    ? "assessment_not_authentic"
                    : "assessment_inconclusive",
            CorrelationId = correlationId,
            CausationId = null,
            OccurredAt = now,
            ReceivedAt = now,
            Classification = "internal",
            Withheld = [],
            Detail = new Dictionary<string, object?>
            {
                ["disposition"] = disposition.ToString(),
                ["check_count"] = checks.Count,
                ["failed_check_count"] = checks.Count(c => c.Outcome == AuthenticityCheckOutcome.Failed),
                ["not_performed_check_count"] = checks.Count(c => c.Outcome == AuthenticityCheckOutcome.NotPerformed),
                ["evidence_version"] = evidenceVersion,
                ["template_id"] = templateId,
                ["template_version_number"] = templateVersionNumber,
                ["ruleset_version"] = rulesetVersion,
            },
        };
    }

    public static AuthenticityAssessmentAuditEvent BuildManuallyOverridden(
        AuditActor actor,
        string documentSessionId,
        string correlationId,
        AuthenticityDisposition originalDisposition,
        AuthenticityDisposition overrideDisposition,
        string reason)
    {
        var now = AuthenticityAssessmentClock.Now();
        return new AuthenticityAssessmentAuditEvent
        {
            EventId = AuthenticityAssessmentEventId.New(),
            TenantId = null,
            Actor = actor,
            Device = null,
            Action = "authenticity_assessment.manually_overridden",
            Target = new AuditTarget("document_session", documentSessionId),
            Outcome = AuditOutcome.Allowed,
            ReasonCode = "manual_override_applied",
            CorrelationId = correlationId,
            CausationId = null,
            OccurredAt = now,
            ReceivedAt = now,
            Classification = "internal",
            Withheld = [],
            Detail = new Dictionary<string, object?>
            {
                ["original_disposition"] = originalDisposition.ToString(),
                ["override_disposition"] = overrideDisposition.ToString(),
                ["reason"] = reason,
            },
        };
    }
}

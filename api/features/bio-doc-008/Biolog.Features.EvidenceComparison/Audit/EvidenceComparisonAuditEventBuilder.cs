namespace Biolog.Features.EvidenceComparison.Audit;

/// <summary>The only sanctioned way to build an <see cref="EvidenceComparisonAuditEvent"/> -
/// mirrors <c>TenantAuditEventBuilder</c>'s shape, scoped to this feature's one action.</summary>
public static class EvidenceComparisonAuditEventBuilder
{
    public static EvidenceComparisonAuditEvent Build(
        AuditActor actor,
        string documentSessionId,
        string reasonCode,
        string correlationId,
        int fieldCount,
        int mismatchCount,
        bool requiresManualReview,
        AuditOutcome outcome)
    {
        var now = EvidenceComparisonClock.Now();
        return new EvidenceComparisonAuditEvent
        {
            EventId = EvidenceComparisonEventId.New(),
            TenantId = null, // no tenant dependency is declared by this story
            Actor = actor,
            Device = null,
            Action = "evidence_comparison.computed",
            Target = new AuditTarget("document_session", documentSessionId),
            Outcome = outcome,
            ReasonCode = reasonCode,
            CorrelationId = correlationId,
            CausationId = null,
            OccurredAt = now,
            ReceivedAt = now,
            Classification = "internal",
            Withheld = [],
            Detail = new Dictionary<string, object?>
            {
                ["field_count"] = fieldCount,
                ["mismatch_count"] = mismatchCount,
                ["requires_manual_review"] = requiresManualReview,
            },
        };
    }
}

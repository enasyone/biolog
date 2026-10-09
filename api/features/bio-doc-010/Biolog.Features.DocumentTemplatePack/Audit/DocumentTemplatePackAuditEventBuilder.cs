using Biolog.Features.AuthenticityAssessment.Model;
using Biolog.Features.DocumentTemplatePack.Model;

namespace Biolog.Features.DocumentTemplatePack.Audit;

/// <summary>The only sanctioned way to build a <see cref="DocumentTemplatePackAuditEvent"/> -
/// mirrors <c>AuthenticityAssessmentAuditEventBuilder</c>'s shape, scoped to this feature's two
/// actions (pack published - AC02, verification recorded - AC04).</summary>
public static class DocumentTemplatePackAuditEventBuilder
{
    public static DocumentTemplatePackAuditEvent BuildPublished(
        AuditActor actor, string packId, string packVersion, int entryCount, string correlationId)
    {
        var now = DocumentTemplatePackClock.Now();
        return new DocumentTemplatePackAuditEvent
        {
            EventId = DocumentTemplatePackEventId.New(),
            TenantId = null, // no tenant dependency is declared by this story
            Actor = actor,
            Device = null,
            Action = "document_template_pack.published",
            Target = new AuditTarget("document_template_pack", packId),
            Outcome = AuditOutcome.Allowed,
            ReasonCode = "pack_published",
            CorrelationId = correlationId,
            CausationId = null,
            OccurredAt = now,
            ReceivedAt = now,
            Classification = "internal",
            Withheld = [],
            Detail = new Dictionary<string, object?>
            {
                ["pack_version"] = packVersion,
                ["entry_count"] = entryCount,
            },
        };
    }

    public static DocumentTemplatePackAuditEvent BuildVerificationRecorded(
        AuditActor actor,
        string packId,
        string packVersion,
        string entryId,
        DocumentTemplateVerificationState state,
        int violationCount,
        string correlationId,
        AuthenticityDisposition? correlatedAuthenticityDisposition)
    {
        var now = DocumentTemplatePackClock.Now();
        return new DocumentTemplatePackAuditEvent
        {
            EventId = DocumentTemplatePackEventId.New(),
            TenantId = null,
            Actor = actor,
            Device = null,
            Action = "document_template_pack.verification_recorded",
            Target = new AuditTarget("document_template_pack_entry", entryId),
            Outcome = AuditOutcome.Allowed,
            ReasonCode = state switch
            {
                DocumentTemplateVerificationState.Consistent => "verification_consistent",
                DocumentTemplateVerificationState.Inconclusive => "verification_inconclusive",
                DocumentTemplateVerificationState.Failed => "verification_failed",
                _ => throw new ArgumentOutOfRangeException(nameof(state), state, "unreachable: exhaustive switch"),
            },
            CorrelationId = correlationId,
            CausationId = null,
            OccurredAt = now,
            ReceivedAt = now,
            Classification = "internal",
            Withheld = [],
            Detail = new Dictionary<string, object?>
            {
                ["pack_id"] = packId,
                ["pack_version"] = packVersion,
                ["entry_id"] = entryId,
                ["state"] = state.ToString(),
                ["violation_count"] = violationCount,
                ["correlated_authenticity_disposition"] = correlatedAuthenticityDisposition?.ToString(),
            },
        };
    }
}

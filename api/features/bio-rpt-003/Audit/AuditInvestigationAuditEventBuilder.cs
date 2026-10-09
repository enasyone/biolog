using System.Text.RegularExpressions;
using Biolog.Features.AuditInvestigation.Domain;
using Biolog.Features.AuditInvestigation.Identifiers;

namespace Biolog.Features.AuditInvestigation.Audit;

/// <summary>The only sanctioned way to build an <see cref="AuditInvestigationAuditEvent"/> - mirrors <c>buildAuditEvent</c> in <c>contracts/js/audit.mjs</c> (ADR-0003), the same local, right-sized port every sibling story's own builder already established (<c>RbacAuditEventBuilder</c>, <c>ProductionAuditEventBuilder</c>, <c>ReportAuditEventBuilder</c>). Registers <c>public</c>/<c>internal</c> only - this feature's own detail never carries anything more sensitive than a row count and a filter summary.</summary>
public static partial class AuditInvestigationAuditEventBuilder
{
    [GeneratedRegex(@"^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$")]
    private static partial Regex ActionPattern();

    private static readonly Dictionary<string, int> ClassificationRank = new(StringComparer.Ordinal)
    {
        ["public"] = 0,
        ["internal"] = 1,
    };

    public static AuditInvestigationAuditEvent Build(
        string tenantId,
        AuditActor actor,
        string action,
        AuditTarget target,
        AuditOutcome outcome,
        string reasonCode,
        string correlationId,
        string? causationId,
        IReadOnlyDictionary<string, (object? Value, string Classification)> detail)
    {
        if (!ActionPattern().IsMatch(action))
        {
            throw new AuditInvestigationError($"action must be a namespaced verb, got '{action}'", "invalid_action", 500);
        }

        if (string.IsNullOrEmpty(target.Type))
        {
            throw new AuditInvestigationError("target must have a non-empty type", "invalid_target", 500);
        }

        if (string.IsNullOrEmpty(reasonCode))
        {
            throw new AuditInvestigationError("reason_code is required on every outcome, including ALLOWED", "invalid_reason_code", 500);
        }

        if (!CanonicalId.IsEventId(correlationId))
        {
            throw new AuditInvestigationError("correlation_id must be a canonical event identifier", "invalid_correlation_id", 500);
        }

        if (causationId is not null && !CanonicalId.IsEventId(causationId))
        {
            throw new AuditInvestigationError("causation_id must be null or a canonical event identifier", "invalid_causation_id", 500);
        }

        var classification = "public";
        var projectedDetail = new Dictionary<string, object?>();
        foreach (var (key, (value, fieldClassification)) in detail)
        {
            if (!ClassificationRank.ContainsKey(fieldClassification))
            {
                throw new AuditInvestigationError($"unregistered classification '{fieldClassification}' for detail field '{key}'", "invalid_classification", 500);
            }

            projectedDetail[key] = value;
            if (ClassificationRank[fieldClassification] > ClassificationRank[classification])
            {
                classification = fieldClassification;
            }
        }

        var now = UtcInstant.Now();
        return new AuditInvestigationAuditEvent
        {
            EventId = CanonicalId.NewEventId(),
            TenantId = tenantId,
            Actor = actor,
            Device = null,
            Action = action,
            Target = target,
            Outcome = outcome,
            ReasonCode = reasonCode,
            CorrelationId = correlationId,
            CausationId = causationId,
            OccurredAt = now,
            ReceivedAt = now,
            Classification = classification,
            Withheld = [],
            Detail = projectedDetail,
        };
    }
}

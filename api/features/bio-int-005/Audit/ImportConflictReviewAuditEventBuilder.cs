using System.Text.RegularExpressions;
using Biolog.Features.ImportConflictReview.Domain;
using Biolog.Features.ImportConflictReview.Identifiers;

namespace Biolog.Features.ImportConflictReview.Audit;

/// <summary>
/// The only sanctioned way to build an <see cref="ImportConflictReviewAuditEvent"/> - mirrors
/// <c>buildAuditEvent</c>'s validation in <c>contracts/js/audit.mjs</c> (ADR-0003), a local,
/// right-sized port, not the shared contract itself.
///
/// This feature's audit <c>Detail</c> never carries a compared field VALUE (an existing or
/// incoming PERSON field like a name or date of birth) - only the external key, person id,
/// mapping id, and the WIRE LABEL of which person field disagreed, mirroring BIO-IDN-005's own
/// "audit trail privacy" discipline exactly. This builder therefore only ever registers
/// <c>public</c>/<c>internal</c> - an attempt to smuggle an actual field value into
/// <c>detail</c> fails closed with <c>invalid_classification</c> rather than silently accepting
/// it.
/// </summary>
public static partial class ImportConflictReviewAuditEventBuilder
{
    [GeneratedRegex(@"^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$")]
    private static partial Regex ActionPattern();

    private static readonly Dictionary<string, int> ClassificationRank = new(StringComparer.Ordinal)
    {
        ["public"] = 0,
        ["internal"] = 1,
    };

    public static ImportConflictReviewAuditEvent Build(
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
            throw new ImportConflictReviewError($"action must be a namespaced verb, got '{action}'", "invalid_action", 500);
        }

        if (string.IsNullOrEmpty(target.Type))
        {
            throw new ImportConflictReviewError("target must have a non-empty type", "invalid_target", 500);
        }

        if (string.IsNullOrEmpty(reasonCode))
        {
            throw new ImportConflictReviewError("reason_code is required on every outcome, including ALLOWED", "invalid_reason_code", 500);
        }

        if (!CanonicalId.IsEventId(correlationId))
        {
            throw new ImportConflictReviewError("correlation_id must be a canonical event identifier", "invalid_correlation_id", 500);
        }

        if (causationId is not null && !CanonicalId.IsEventId(causationId))
        {
            throw new ImportConflictReviewError("causation_id must be null or a canonical event identifier", "invalid_causation_id", 500);
        }

        var classification = "public";
        var projectedDetail = new Dictionary<string, object?>();
        foreach (var (key, (value, fieldClassification)) in detail)
        {
            if (!ClassificationRank.ContainsKey(fieldClassification))
            {
                // Fail closed, not open: an unregistered classification (e.g. an attempt to
                // smuggle "personal"/"sensitive"/"secret" through) is never treated as safe.
                throw new ImportConflictReviewError(
                    $"unregistered classification '{fieldClassification}' for detail field '{key}'", "invalid_classification", 500);
            }

            projectedDetail[key] = value;
            if (ClassificationRank[fieldClassification] > ClassificationRank[classification])
            {
                classification = fieldClassification;
            }
        }

        var now = UtcInstant.Now();
        return new ImportConflictReviewAuditEvent
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

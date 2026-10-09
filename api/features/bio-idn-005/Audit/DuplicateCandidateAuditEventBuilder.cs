using System.Text.RegularExpressions;
using Biolog.Features.DuplicateReview.Domain;
using Biolog.Features.DuplicateReview.Identifiers;

namespace Biolog.Features.DuplicateReview.Audit;

/// <summary>
/// The only sanctioned way to build a <see cref="DuplicateCandidateAuditEvent"/> - mirrors
/// <c>buildAuditEvent</c>'s validation in <c>contracts/js/audit.mjs</c> (ADR-0003), a local,
/// right-sized port, not the shared contract itself.
///
/// This feature's audit <c>Detail</c> never carries a compared PII VALUE (a name or date of
/// birth) - only person ids, which classification ranks are the same as this codebase's
/// <c>internal</c> tier (a canonical id carries no authority - ADR-0001 §1.5), and the WIRE
/// LABEL of which signal source matched (e.g. <c>"NAME_AND_DATE_OF_BIRTH"</c>), which says what
/// KIND of evidence was found without saying what the matching name or date of birth actually
/// was. This builder therefore only ever registers <c>public</c>/<c>internal</c> - it does not
/// register <c>personal</c>, <c>sensitive</c> or <c>secret</c>, so an attempt to smuggle an
/// actual name/DOB value into <c>detail</c> fails closed with <c>invalid_classification</c>
/// rather than silently accepting it (see <see cref="DuplicateReviewService"/>'s "Audit trail
/// privacy" note).
/// </summary>
public static partial class DuplicateCandidateAuditEventBuilder
{
    [GeneratedRegex(@"^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$")]
    private static partial Regex ActionPattern();

    private static readonly Dictionary<string, int> ClassificationRank = new(StringComparer.Ordinal)
    {
        ["public"] = 0,
        ["internal"] = 1,
    };

    public static DuplicateCandidateAuditEvent Build(
        string tenantId,
        AuditActor actor,
        string action,
        AuditTarget target,
        AuditOutcome outcome,
        string reasonCode,
        string correlationId,
        string? causationId,
        IReadOnlyDictionary<string, (object? Value, string Classification)> detail,
        IReadOnlyList<string> withheld)
    {
        if (!ActionPattern().IsMatch(action))
        {
            throw new DuplicateCandidateError($"action must be a namespaced verb, got '{action}'", "invalid_action", 500);
        }

        if (string.IsNullOrEmpty(target.Type))
        {
            throw new DuplicateCandidateError("target must have a non-empty type", "invalid_target", 500);
        }

        if (string.IsNullOrEmpty(reasonCode))
        {
            throw new DuplicateCandidateError("reason_code is required on every outcome, including ALLOWED", "invalid_reason_code", 500);
        }

        if (!CanonicalId.IsEventId(correlationId))
        {
            throw new DuplicateCandidateError("correlation_id must be a canonical event identifier", "invalid_correlation_id", 500);
        }

        if (causationId is not null && !CanonicalId.IsEventId(causationId))
        {
            throw new DuplicateCandidateError("causation_id must be null or a canonical event identifier", "invalid_causation_id", 500);
        }

        var classification = "public";
        var projectedDetail = new Dictionary<string, object?>();
        foreach (var (key, (value, fieldClassification)) in detail)
        {
            if (!ClassificationRank.ContainsKey(fieldClassification))
            {
                // Fail closed, not open: an unregistered classification (e.g. an attempt to
                // smuggle "personal"/"sensitive"/"secret" through) is never treated as safe.
                throw new DuplicateCandidateError(
                    $"unregistered classification '{fieldClassification}' for detail field '{key}'", "invalid_classification", 500);
            }

            projectedDetail[key] = value;
            if (ClassificationRank[fieldClassification] > ClassificationRank[classification])
            {
                classification = fieldClassification;
            }
        }

        var now = UtcInstant.Now();
        return new DuplicateCandidateAuditEvent
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
            Withheld = withheld,
            Detail = projectedDetail,
        };
    }
}

using System.Text.RegularExpressions;
using Biolog.Features.CardLifecycle.Domain;
using Biolog.Features.CardLifecycle.Identifiers;

namespace Biolog.Features.CardLifecycle.Audit;

/// <summary>
/// The only sanctioned way to build a <see cref="CardLifecycleAuditEvent"/> - mirrors
/// <c>buildAuditEvent</c>'s validation in <c>contracts/js/audit.mjs</c> (ADR-0003), the same
/// local, right-sized port BIO-CRD-005's own builder already established.
///
/// Registers <c>public</c>/<c>internal</c> only. This feature's own detail (card class,
/// sequence number, reason codes, opaque device references) carries no raw PII/biometric
/// content, so it is safe to carry as <c>internal</c> directly. An unregistered classification
/// fails closed (see below).
/// </summary>
public static partial class CardLifecycleAuditEventBuilder
{
    [GeneratedRegex(@"^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$")]
    private static partial Regex ActionPattern();

    private static readonly Dictionary<string, int> ClassificationRank = new(StringComparer.Ordinal)
    {
        ["public"] = 0,
        ["internal"] = 1,
    };

    public static CardLifecycleAuditEvent Build(
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
            throw new CardLifecycleError($"action must be a namespaced verb, got '{action}'", "invalid_action", 500);
        }

        if (string.IsNullOrEmpty(target.Type))
        {
            throw new CardLifecycleError("target must have a non-empty type", "invalid_target", 500);
        }

        if (string.IsNullOrEmpty(reasonCode))
        {
            throw new CardLifecycleError("reason_code is required on every outcome, including ALLOWED", "invalid_reason_code", 500);
        }

        if (!CanonicalId.IsEventId(correlationId))
        {
            throw new CardLifecycleError("correlation_id must be a canonical event identifier", "invalid_correlation_id", 500);
        }

        if (causationId is not null && !CanonicalId.IsEventId(causationId))
        {
            throw new CardLifecycleError("causation_id must be null or a canonical event identifier", "invalid_causation_id", 500);
        }

        var classification = "public";
        var projectedDetail = new Dictionary<string, object?>();
        foreach (var (key, (value, fieldClassification)) in detail)
        {
            if (!ClassificationRank.ContainsKey(fieldClassification))
            {
                throw new CardLifecycleError(
                    $"unregistered classification '{fieldClassification}' for detail field '{key}'", "invalid_classification", 500);
            }

            projectedDetail[key] = value;
            if (ClassificationRank[fieldClassification] > ClassificationRank[classification])
            {
                classification = fieldClassification;
            }
        }

        var now = UtcInstant.Now();
        return new CardLifecycleAuditEvent
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

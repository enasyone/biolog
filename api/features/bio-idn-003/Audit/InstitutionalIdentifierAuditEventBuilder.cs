using System.Text.RegularExpressions;
using Biolog.Features.InstitutionalIdentifiers.Domain;
using Biolog.Features.InstitutionalIdentifiers.Identifiers;

namespace Biolog.Features.InstitutionalIdentifiers.Audit;

/// <summary>
/// The only sanctioned way to build an <see cref="InstitutionalIdentifierAuditEvent"/> - mirrors
/// <c>buildAuditEvent</c>'s validation in <c>contracts/js/audit.mjs</c> (ADR-0003), a local,
/// right-sized port, not the shared contract itself.
///
/// Unlike BIO-IDN-002's builder (whose <c>ClassificationRank</c> only ever needed
/// <c>public</c>/<c>internal</c>, since profile data is never PII), this feature handles a
/// business identifier VALUE that <c>contracts/schemas/data-classification.json</c> names
/// explicitly: <c>"personal"</c> (rank 2) lists "institutional identifier" as a worked example,
/// alongside name and date of birth. This builder therefore also registers <c>personal</c>,
/// matching BIO-IDN-001's <c>PersonAuditEventBuilder</c> exactly. It deliberately does NOT
/// register <c>sensitive</c> or <c>secret</c> - this feature has no field at either rank, and an
/// unregistered classification fails closed (see below).
///
/// <see cref="InstitutionalIdentifierService"/> never actually supplies the identifier's
/// <c>Value</c> to this builder's <c>detail</c> parameter - it always lists <c>"value"</c> in
/// <paramref name="withheld"/> instead (see that service's header "Audit trail privacy" and
/// BIO-IDN-001's <c>PersonAuditEventBuilder</c> for the identical, deliberately stricter,
/// posture).
/// </summary>
public static partial class InstitutionalIdentifierAuditEventBuilder
{
    [GeneratedRegex(@"^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$")]
    private static partial Regex ActionPattern();

    private static readonly Dictionary<string, int> ClassificationRank = new(StringComparer.Ordinal)
    {
        ["public"] = 0,
        ["internal"] = 1,
        ["personal"] = 2,
    };

    public static InstitutionalIdentifierAuditEvent Build(
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
            throw new InstitutionalIdentifierError($"action must be a namespaced verb, got '{action}'", "invalid_action", 500);
        }

        if (string.IsNullOrEmpty(target.Type))
        {
            throw new InstitutionalIdentifierError("target must have a non-empty type", "invalid_target", 500);
        }

        if (string.IsNullOrEmpty(reasonCode))
        {
            throw new InstitutionalIdentifierError("reason_code is required on every outcome, including ALLOWED", "invalid_reason_code", 500);
        }

        if (!CanonicalId.IsEventId(correlationId))
        {
            throw new InstitutionalIdentifierError("correlation_id must be a canonical event identifier", "invalid_correlation_id", 500);
        }

        if (causationId is not null && !CanonicalId.IsEventId(causationId))
        {
            throw new InstitutionalIdentifierError("causation_id must be null or a canonical event identifier", "invalid_causation_id", 500);
        }

        var classification = "public";
        var projectedDetail = new Dictionary<string, object?>();
        foreach (var (key, (value, fieldClassification)) in detail)
        {
            if (!ClassificationRank.ContainsKey(fieldClassification))
            {
                // Fail closed, not open: an unregistered classification (e.g. a future
                // attempt to smuggle "sensitive"/"secret" through) is never treated as safe.
                throw new InstitutionalIdentifierError(
                    $"unregistered classification '{fieldClassification}' for detail field '{key}'", "invalid_classification", 500);
            }

            projectedDetail[key] = value;
            if (ClassificationRank[fieldClassification] > ClassificationRank[classification])
            {
                classification = fieldClassification;
            }
        }

        var now = UtcInstant.Now();
        return new InstitutionalIdentifierAuditEvent
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

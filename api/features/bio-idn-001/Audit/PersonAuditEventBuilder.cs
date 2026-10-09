using System.Text.RegularExpressions;
using Biolog.Features.PersonRegistry.Domain;
using Biolog.Features.PersonRegistry.Identifiers;

namespace Biolog.Features.PersonRegistry.Audit;

/// <summary>
/// The only sanctioned way to build a <see cref="PersonAuditEvent"/> - mirrors
/// <c>buildAuditEvent</c>'s validation in <c>contracts/js/audit.mjs</c> (ADR-0003), scoped to
/// the fields this feature actually populates. A local, right-sized port of that contract's
/// rules, not the shared contract itself - see <c>TenantDomainAuditEventBuilder</c>'s header
/// for why this is a deliberately separate, non-shared copy rather than a library.
///
/// Unlike BIO-TEN-002's builder (whose <c>ClassificationRank</c> only ever needed
/// <c>public</c>/<c>internal</c>, since domain-mapping data is never PII), PERSON handles
/// personal data (<c>contracts/schemas/data-classification.json</c>: "name ... date of birth"
/// are <c>personal</c>, rank 2). This builder therefore also registers <c>personal</c> and
/// <c>sensitive</c>. It deliberately does NOT register <c>secret</c> (rank 4, "envelope:
/// never") - PERSON has no secret-classified field, and an unregistered classification fails
/// closed (see below), so a future bug that tried to pass one through would be caught here
/// rather than silently accepted.
///
/// <see cref="PersonService"/> never actually supplies a <c>personal</c>-classified VALUE to
/// this builder's <c>detail</c> parameter, by its own deliberate choice - see that class's
/// header "Audit trail privacy" for why every PII field name is instead listed in
/// <paramref name="withheld"/> on every event this feature ever produces, and why that is a
/// stricter, more privacy-conscious posture than the schema's own <c>allowlist_only</c>
/// envelope for <c>personal</c> data technically requires.
/// </summary>
public static partial class PersonAuditEventBuilder
{
    [GeneratedRegex(@"^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$")]
    private static partial Regex ActionPattern();

    private static readonly Dictionary<string, int> ClassificationRank = new(StringComparer.Ordinal)
    {
        ["public"] = 0,
        ["internal"] = 1,
        ["personal"] = 2,
        ["sensitive"] = 3,
    };

    public static PersonAuditEvent Build(
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
            throw new PersonError($"action must be a namespaced verb, got '{action}'", "invalid_action", 500);
        }

        if (string.IsNullOrEmpty(target.Type))
        {
            throw new PersonError("target must have a non-empty type", "invalid_target", 500);
        }

        if (string.IsNullOrEmpty(reasonCode))
        {
            throw new PersonError("reason_code is required on every outcome, including ALLOWED", "invalid_reason_code", 500);
        }

        if (!CanonicalId.IsEventId(correlationId))
        {
            throw new PersonError("correlation_id must be a canonical event identifier", "invalid_correlation_id", 500);
        }

        if (causationId is not null && !CanonicalId.IsEventId(causationId))
        {
            throw new PersonError("causation_id must be null or a canonical event identifier", "invalid_causation_id", 500);
        }

        var classification = "public";
        var projectedDetail = new Dictionary<string, object?>();
        foreach (var (key, (value, fieldClassification)) in detail)
        {
            if (!ClassificationRank.ContainsKey(fieldClassification))
            {
                // Fail closed, not open: an unregistered classification is never treated as
                // safe - this is also where a future attempt to pass "secret" through would
                // be caught, since it is deliberately never registered above.
                throw new PersonError(
                    $"unregistered classification '{fieldClassification}' for detail field '{key}'", "invalid_classification", 500);
            }

            projectedDetail[key] = value;
            if (ClassificationRank[fieldClassification] > ClassificationRank[classification])
            {
                classification = fieldClassification;
            }
        }

        var now = UtcInstant.Now();
        return new PersonAuditEvent
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

using System.Text.RegularExpressions;
using Biolog.Features.PersonLifecycle.Identifiers;

namespace Biolog.Features.PersonLifecycle.Audit;

/// <summary>
/// The only sanctioned way to build a <see cref="PersonLifecycleAuditEvent"/> - mirrors
/// <c>buildAuditEvent</c>'s validation in <c>contracts/js/audit.mjs</c> (ADR-0003), a local,
/// right-sized port, not the shared contract itself.
///
/// This feature never touches PERSON's own PII (name, date of birth) at all - it only ever
/// sees <see cref="Domain.PersonStatus"/> wire strings and a caller-supplied free-text
/// <c>reason</c>. <c>ClassificationRank</c> therefore only registers <c>public</c>/<c>internal</c>,
/// matching BIO-IDN-002's and BIO-IDN-003's builders rather than BIO-IDN-001's. The free-text
/// <c>reason</c> is classified <c>internal</c> and included in <c>Detail</c>, matching this
/// codebase's established treatment of other caller-supplied free text (BIO-IDN-002's
/// <c>custom_label</c>, BIO-IDN-003's tenant-defined identifier labels) - callers must not put
/// PII in it, documented in <c>docs/features/bio-idn-004/README.md</c>.
/// </summary>
public static partial class PersonLifecycleAuditEventBuilder
{
    [GeneratedRegex(@"^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$")]
    private static partial Regex ActionPattern();

    private static readonly Dictionary<string, int> ClassificationRank = new(StringComparer.Ordinal)
    {
        ["public"] = 0,
        ["internal"] = 1,
    };

    public static PersonLifecycleAuditEvent Build(
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
            throw new Domain.PersonLifecycleError($"action must be a namespaced verb, got '{action}'", "invalid_action", 500);
        }

        if (string.IsNullOrEmpty(target.Type))
        {
            throw new Domain.PersonLifecycleError("target must have a non-empty type", "invalid_target", 500);
        }

        if (string.IsNullOrEmpty(reasonCode))
        {
            throw new Domain.PersonLifecycleError("reason_code is required on every outcome, including ALLOWED", "invalid_reason_code", 500);
        }

        if (!CanonicalId.IsEventId(correlationId))
        {
            throw new Domain.PersonLifecycleError("correlation_id must be a canonical event identifier", "invalid_correlation_id", 500);
        }

        if (causationId is not null && !CanonicalId.IsEventId(causationId))
        {
            throw new Domain.PersonLifecycleError("causation_id must be null or a canonical event identifier", "invalid_causation_id", 500);
        }

        var classification = "public";
        var projectedDetail = new Dictionary<string, object?>();
        foreach (var (key, (value, fieldClassification)) in detail)
        {
            if (!ClassificationRank.ContainsKey(fieldClassification))
            {
                throw new Domain.PersonLifecycleError(
                    $"unregistered classification '{fieldClassification}' for detail field '{key}'", "invalid_classification", 500);
            }

            projectedDetail[key] = value;
            if (ClassificationRank[fieldClassification] > ClassificationRank[classification])
            {
                classification = fieldClassification;
            }
        }

        var now = UtcInstant.Now();
        return new PersonLifecycleAuditEvent
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

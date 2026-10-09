using System.Text.RegularExpressions;
using Biolog.Features.Rbac.Domain;
using Biolog.Features.Rbac.Identifiers;

namespace Biolog.Features.Rbac.Audit;

/// <summary>
/// The only sanctioned way to build an <see cref="RbacAuditEvent"/> - mirrors
/// <c>buildAuditEvent</c>'s validation in <c>contracts/js/audit.mjs</c> (ADR-0003), a local,
/// right-sized port, not the shared contract itself.
///
/// This feature's audit <c>Detail</c> never carries a value that could itself grant authority
/// (a permission key IS registered here as <c>internal</c>, since knowing WHICH permission a
/// grant applies to is operationally necessary and not itself sensitive the way a name or an
/// identifier VALUE would be - it is metadata about the shape of an authorization decision, not
/// PII). This builder therefore only ever registers <c>public</c>/<c>internal</c> - it does not
/// register <c>personal</c>, <c>sensitive</c> or <c>secret</c>.
/// </summary>
public static partial class RbacAuditEventBuilder
{
    [GeneratedRegex(@"^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$")]
    private static partial Regex ActionPattern();

    private static readonly Dictionary<string, int> ClassificationRank = new(StringComparer.Ordinal)
    {
        ["public"] = 0,
        ["internal"] = 1,
    };

    public static RbacAuditEvent Build(
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
            throw new RbacError($"action must be a namespaced verb, got '{action}'", "invalid_action", 500);
        }

        if (string.IsNullOrEmpty(target.Type))
        {
            throw new RbacError("target must have a non-empty type", "invalid_target", 500);
        }

        if (string.IsNullOrEmpty(reasonCode))
        {
            throw new RbacError("reason_code is required on every outcome, including ALLOWED", "invalid_reason_code", 500);
        }

        if (!CanonicalId.IsEventId(correlationId))
        {
            throw new RbacError("correlation_id must be a canonical event identifier", "invalid_correlation_id", 500);
        }

        if (causationId is not null && !CanonicalId.IsEventId(causationId))
        {
            throw new RbacError("causation_id must be null or a canonical event identifier", "invalid_causation_id", 500);
        }

        var classification = "public";
        var projectedDetail = new Dictionary<string, object?>();
        foreach (var (key, (value, fieldClassification)) in detail)
        {
            if (!ClassificationRank.ContainsKey(fieldClassification))
            {
                // Fail closed, not open: an unregistered classification is never treated as
                // safe.
                throw new RbacError($"unregistered classification '{fieldClassification}' for detail field '{key}'", "invalid_classification", 500);
            }

            projectedDetail[key] = value;
            if (ClassificationRank[fieldClassification] > ClassificationRank[classification])
            {
                classification = fieldClassification;
            }
        }

        var now = UtcInstant.Now();
        return new RbacAuditEvent
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

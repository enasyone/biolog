using System.Text.RegularExpressions;
using Biolog.Features.TenantFeatureConfig.Domain;
using Biolog.Features.TenantFeatureConfig.Identifiers;

namespace Biolog.Features.TenantFeatureConfig.Audit;

/// <summary>
/// The only sanctioned way to build a <see cref="TenantFeatureConfigurationAuditEvent"/> -
/// mirrors <c>buildAuditEvent</c>'s validation in <c>contracts/js/audit.mjs</c> (ADR-0003),
/// copied and adapted from <c>api/features/bio-ten-001/Audit/TenantAuditEventBuilder.cs</c>
/// (that copy requires <see cref="TenantId"/> here, instead of always nulling it).
/// </summary>
public static partial class TenantFeatureConfigurationAuditEventBuilder
{
    [GeneratedRegex(@"^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$")]
    private static partial Regex ActionPattern();

    private static readonly Dictionary<string, int> ClassificationRank = new(StringComparer.Ordinal)
    {
        ["public"] = 0,
        ["internal"] = 1,
    };

    public static TenantFeatureConfigurationAuditEvent Build(
        AuditActor actor,
        string action,
        string tenantId,
        AuditTarget target,
        AuditOutcome outcome,
        string reasonCode,
        string correlationId,
        string? causationId,
        IReadOnlyDictionary<string, (object? Value, string Classification)> detail)
    {
        if (!ActionPattern().IsMatch(action))
        {
            throw new TenantFeatureConfigurationError($"action must be a namespaced verb, got '{action}'", "invalid_action", 500);
        }

        if (string.IsNullOrEmpty(target.Type))
        {
            throw new TenantFeatureConfigurationError("target must have a non-empty type", "invalid_target", 500);
        }

        if (string.IsNullOrEmpty(reasonCode))
        {
            throw new TenantFeatureConfigurationError("reason_code is required on every outcome, including ALLOWED", "invalid_reason_code", 500);
        }

        if (!CanonicalId.IsEventId(correlationId))
        {
            throw new TenantFeatureConfigurationError("correlation_id must be a canonical event identifier", "invalid_correlation_id", 500);
        }

        if (causationId is not null && !CanonicalId.IsEventId(causationId))
        {
            throw new TenantFeatureConfigurationError("causation_id must be null or a canonical event identifier", "invalid_causation_id", 500);
        }

        var classification = "public";
        var projectedDetail = new Dictionary<string, object?>();
        foreach (var (key, (value, fieldClassification)) in detail)
        {
            if (!ClassificationRank.ContainsKey(fieldClassification))
            {
                throw new TenantFeatureConfigurationError(
                    $"unregistered classification '{fieldClassification}' for detail field '{key}'", "invalid_classification", 500);
            }

            projectedDetail[key] = value;
            if (ClassificationRank[fieldClassification] > ClassificationRank[classification])
            {
                classification = fieldClassification;
            }
        }

        var now = UtcInstant.Now();
        return new TenantFeatureConfigurationAuditEvent
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

using System.Text.RegularExpressions;
using Biolog.Features.TenantRegistry.Domain;
using Biolog.Features.TenantRegistry.Identifiers;

namespace Biolog.Features.TenantRegistry.Audit;

/// <summary>
/// The only sanctioned way to build a <see cref="TenantAuditEvent"/> - mirrors
/// <c>buildAuditEvent</c>'s validation in <c>contracts/js/audit.mjs</c> (ADR-0003), scoped
/// to the fields this feature actually populates. This is a local, right-sized
/// implementation of that contract's rules, not the shared contract itself: no generated
/// .NET binding exists yet (see <see cref="Identifiers.CanonicalId"/>'s header note).
///
/// Detail values are supplied already allowlisted by the caller (<see cref="TenantService"/>)
/// as <c>(value, classification)</c> pairs; this builder's job is to enforce the envelope's
/// own required-field and format rules and to compute the envelope's classification as the
/// highest of what was supplied - the same "classification is the maximum" rule
/// <c>contracts/schemas/data-classification.json</c> states. It does not reimplement
/// <c>audit.mjs</c>'s recursive/collision-safe allowlist projection: every field this
/// feature ever audits is a flat, known-safe scalar on <see cref="Tenant"/> (BIO-TEN-001-AC02),
/// so there is no nested or caller-shaped source object for that machinery to protect against.
/// </summary>
public static partial class TenantAuditEventBuilder
{
    [GeneratedRegex(@"^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$")]
    private static partial Regex ActionPattern();

    private static readonly Dictionary<string, int> ClassificationRank = new(StringComparer.Ordinal)
    {
        ["public"] = 0,
        ["internal"] = 1,
    };

    public static TenantAuditEvent Build(
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
            throw new TenantRegistryError($"action must be a namespaced verb, got '{action}'", "invalid_action", 500);
        }

        if (string.IsNullOrEmpty(target.Type))
        {
            throw new TenantRegistryError("target must have a non-empty type", "invalid_target", 500);
        }

        if (string.IsNullOrEmpty(reasonCode))
        {
            throw new TenantRegistryError("reason_code is required on every outcome, including ALLOWED", "invalid_reason_code", 500);
        }

        if (!CanonicalId.IsEventId(correlationId))
        {
            throw new TenantRegistryError("correlation_id must be a canonical event identifier", "invalid_correlation_id", 500);
        }

        if (causationId is not null && !CanonicalId.IsEventId(causationId))
        {
            throw new TenantRegistryError("causation_id must be null or a canonical event identifier", "invalid_causation_id", 500);
        }

        var classification = "public";
        var projectedDetail = new Dictionary<string, object?>();
        foreach (var (key, (value, fieldClassification)) in detail)
        {
            if (!ClassificationRank.ContainsKey(fieldClassification))
            {
                // Fail closed, not open: an unregistered classification is never treated as safe.
                throw new TenantRegistryError(
                    $"unregistered classification '{fieldClassification}' for detail field '{key}'", "invalid_classification", 500);
            }

            projectedDetail[key] = value;
            if (ClassificationRank[fieldClassification] > ClassificationRank[classification])
            {
                classification = fieldClassification;
            }
        }

        var now = UtcInstant.Now();
        return new TenantAuditEvent
        {
            EventId = CanonicalId.NewEventId(),
            TenantId = null, // every action this feature audits is control-plane work (ADR-0002 §1.4)
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

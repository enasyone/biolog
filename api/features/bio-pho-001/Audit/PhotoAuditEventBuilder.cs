using System.Text.RegularExpressions;
using Biolog.Features.Photography.Domain;
using Biolog.Features.Photography.Identifiers;

namespace Biolog.Features.Photography.Audit;

/// <summary>
/// The only sanctioned way to build a <see cref="PhotoAuditEvent"/> - mirrors
/// <c>buildAuditEvent</c>'s validation in <c>contracts/js/audit.mjs</c> (ADR-0003), a local,
/// right-sized port, not the shared contract itself.
///
/// <b>ADR-0003 Decision 3's classification table names "photo reference" explicitly as
/// <c>personal</c> (allowlist only) and "raw biometric images"/"biometric templates" as
/// <c>sensitive</c>/<c>secret</c> (never).</b> This builder therefore only ever registers
/// <c>internal</c> for the technical metadata it carries (version numbers, format, pixel
/// dimensions, derivative kind) - identifying WHICH photo a mutation touched, never a copy of
/// the photo itself. <see cref="PhotoMaster.Content"/> / <see cref="PhotoDerivative.Content"/>
/// (the raw image bytes) are never passed to <see cref="Build"/> at all - not even under a
/// <c>secret</c> classification for this builder to withhold, since a <c>secret</c> value must
/// never be COPIED into an envelope in any form (ADR-0003: "not masked, not truncated, not
/// hashed"). <see cref="AuditTarget.Id"/> carries <c>PersonId</c>, exactly like every sibling
/// story's <c>new AuditTarget("person", personId)</c> - Target identifies the record an event
/// is about and is not subject to Detail's allowlist, the same structural split every sibling
/// audit builder already uses.
/// </summary>
public static partial class PhotoAuditEventBuilder
{
    [GeneratedRegex(@"^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$")]
    private static partial Regex ActionPattern();

    private static readonly Dictionary<string, int> ClassificationRank = new(StringComparer.Ordinal)
    {
        ["public"] = 0,
        ["internal"] = 1,
    };

    public static PhotoAuditEvent Build(
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
            throw new PhotoError($"action must be a namespaced verb, got '{action}'", "invalid_action", 500);
        }

        if (string.IsNullOrEmpty(target.Type))
        {
            throw new PhotoError("target must have a non-empty type", "invalid_target", 500);
        }

        if (string.IsNullOrEmpty(reasonCode))
        {
            throw new PhotoError("reason_code is required on every outcome, including ALLOWED", "invalid_reason_code", 500);
        }

        if (!CanonicalId.IsEventId(correlationId))
        {
            throw new PhotoError("correlation_id must be a canonical event identifier", "invalid_correlation_id", 500);
        }

        if (causationId is not null && !CanonicalId.IsEventId(causationId))
        {
            throw new PhotoError("causation_id must be null or a canonical event identifier", "invalid_causation_id", 500);
        }

        var classification = "public";
        var projectedDetail = new Dictionary<string, object?>();
        foreach (var (key, (value, fieldClassification)) in detail)
        {
            if (!ClassificationRank.ContainsKey(fieldClassification))
            {
                // Fail closed, not open: an unregistered classification is never treated as
                // safe. In particular this rejects "personal"/"sensitive"/"secret" outright -
                // this builder has no allowlist projection for those levels at all, so a
                // caller cannot smuggle a photo reference or photo bytes through Detail by
                // simply naming a higher classification.
                throw new PhotoError($"unregistered classification '{fieldClassification}' for detail field '{key}'", "invalid_classification", 500);
            }

            projectedDetail[key] = value;
            if (ClassificationRank[fieldClassification] > ClassificationRank[classification])
            {
                classification = fieldClassification;
            }
        }

        var now = UtcInstant.Now();
        return new PhotoAuditEvent
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

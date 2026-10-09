using System.Text.RegularExpressions;
using Biolog.Features.PhotoQuality.Domain;
using Biolog.Features.PhotoQuality.Identifiers;

namespace Biolog.Features.PhotoQuality.Audit;

/// <summary>
/// The only sanctioned way to build a <see cref="PhotoQualityAuditEvent"/> - mirrors
/// <c>buildAuditEvent</c>'s validation in <c>contracts/js/audit.mjs</c> (ADR-0003), a local,
/// right-sized port, not the shared contract itself.
///
/// ADR-0003 Decision 3's classification table names "photo reference" as <c>personal</c>
/// (allowlist only) and raw biometric/photo images as <c>sensitive</c>/<c>secret</c> (never).
/// This builder therefore only ever registers <c>internal</c> for the technical metadata it
/// carries (sequence numbers, dimensions, DPI, print/biometric pass-fail reasons) - never the
/// raw capture bytes, which are never even passed to <see cref="Build"/>. The same structural
/// split <c>PhotoAuditEventBuilder</c> (BIO-PHO-001) and <c>CaptureAuditEventBuilder</c>
/// (BIO-PHO-002) already established.
/// </summary>
public static partial class PhotoQualityAuditEventBuilder
{
    [GeneratedRegex(@"^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$")]
    private static partial Regex ActionPattern();

    private static readonly Dictionary<string, int> ClassificationRank = new(StringComparer.Ordinal)
    {
        ["public"] = 0,
        ["internal"] = 1,
    };

    public static PhotoQualityAuditEvent Build(
        string tenantId,
        AuditActor actor,
        string action,
        AuditTarget target,
        AuditOutcome outcome,
        string reasonCode,
        string correlationId,
        string? causationId,
        IReadOnlyDictionary<string, (object? Value, string Classification)> detail,
        string? eventId = null)
    {
        if (eventId is not null && !CanonicalId.IsEventId(eventId))
        {
            throw new PhotoQualityError("eventId must be null or a canonical event identifier", "invalid_event_id", 500);
        }

        if (!ActionPattern().IsMatch(action))
        {
            throw new PhotoQualityError($"action must be a namespaced verb, got '{action}'", "invalid_action", 500);
        }

        if (string.IsNullOrEmpty(target.Type))
        {
            throw new PhotoQualityError("target must have a non-empty type", "invalid_target", 500);
        }

        if (string.IsNullOrEmpty(reasonCode))
        {
            throw new PhotoQualityError("reason_code is required on every outcome, including ALLOWED", "invalid_reason_code", 500);
        }

        if (!CanonicalId.IsEventId(correlationId))
        {
            throw new PhotoQualityError("correlation_id must be a canonical event identifier", "invalid_correlation_id", 500);
        }

        if (causationId is not null && !CanonicalId.IsEventId(causationId))
        {
            throw new PhotoQualityError("causation_id must be null or a canonical event identifier", "invalid_causation_id", 500);
        }

        var classification = "public";
        var projectedDetail = new Dictionary<string, object?>();
        foreach (var (key, (value, fieldClassification)) in detail)
        {
            if (!ClassificationRank.ContainsKey(fieldClassification))
            {
                throw new PhotoQualityError($"unregistered classification '{fieldClassification}' for detail field '{key}'", "invalid_classification", 500);
            }

            projectedDetail[key] = value;
            if (ClassificationRank[fieldClassification] > ClassificationRank[classification])
            {
                classification = fieldClassification;
            }
        }

        var now = UtcInstant.Now();
        return new PhotoQualityAuditEvent
        {
            // ZB-PHO003-02: ApproveAsync passes a durably-recorded eventId on a resumed retry so
            // the SAME audit event id is reused instead of minting a fresh one every attempt -
            // see PhotoQualityCapture's own PendingApprovalAuditEventId header.
            EventId = eventId ?? CanonicalId.NewEventId(),
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

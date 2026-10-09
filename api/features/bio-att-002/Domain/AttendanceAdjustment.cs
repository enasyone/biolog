using Biolog.Features.AttendanceSessions.Identifiers;

namespace Biolog.Features.AttendanceSessions.Domain;

/// <summary>
/// AC04 - an administrator's manual override of one roster member's classification for one
/// session, layered ON TOP of <see cref="SessionAttendanceClassifier"/>'s own computed
/// <see cref="SessionMemberClassification"/> - the identical two-tier shape
/// <c>bio-doc-009</c>'s own <c>AuthenticityManualOverride</c> established: a separate, immutable,
/// append-only record that NEVER mutates the thing it overrides. A roster read
/// (<see cref="SessionAttendanceQueryService"/>) always surfaces BOTH the originally-computed
/// status (with its own grounding evidence, AC03) and the latest adjustment, if any - the audit
/// trail is always visible, never silently replaced.
///
/// Mints a registered <c>evt_</c> id (see <c>Identifiers/CanonicalId.cs</c>'s own header) - an
/// adjustment genuinely is an auditable act in its own right, the same choice BIO-ATT-001's own
/// <c>AttendanceEvent</c> and BIO-TRN-002's own <c>TransportEvent</c> make for a captured fact.
/// </summary>
public sealed record AttendanceAdjustment
{
    public required string AdjustmentId { get; init; }
    public required string TenantId { get; init; }
    public required string SessionId { get; init; }
    public required string PersonId { get; init; }
    public required SessionAttendanceStatusValue OriginalStatus { get; init; }
    public required SessionAttendanceStatusValue AdjustedStatus { get; init; }
    public required string Reason { get; init; }
    public required string AdjustedByActorId { get; init; }
    public required string AdjustedAt { get; init; }

    public static AttendanceAdjustment Create(
        string tenantId, string sessionId, string personId, SessionAttendanceStatusValue originalStatus, SessionAttendanceStatusValue adjustedStatus,
        string reason, string actorId, string now)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 500)
        {
            throw SessionError.Validation([new Api.ValidationErrorItem("reason", "invalid", "reason must be non-empty and at most 500 characters")]);
        }

        return new AttendanceAdjustment
        {
            AdjustmentId = CanonicalId.NewEventId(),
            TenantId = tenantId,
            SessionId = sessionId,
            PersonId = personId,
            OriginalStatus = originalStatus,
            AdjustedStatus = adjustedStatus,
            Reason = reason,
            AdjustedByActorId = actorId,
            AdjustedAt = now,
        };
    }
}

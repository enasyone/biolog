using Biolog.Features.WorkforceAttendance.Identifiers;

namespace Biolog.Features.WorkforceAttendance.Domain;

/// <summary>
/// AC04 - an administrator's manual override of one person's derived daily overtime outcome,
/// layered ON TOP OF <see cref="ClockPairEvaluator"/>/<see cref="OvertimeEvaluator"/>'s own
/// computed result - the identical two-tier shape BIO-ATT-002's own <c>AttendanceAdjustment</c>
/// (and, before it, <c>bio-doc-009</c>'s own <c>AuthenticityManualOverride</c>) established: a
/// separate, immutable, append-only record that NEVER mutates the thing it overrides, and NEVER
/// touches BIO-ATT-001's own raw clock events either. A daily summary read
/// (<see cref="WorkforceAttendanceService.GetDailySummaryAsync"/>) always surfaces BOTH the
/// originally-computed overtime (with its own grounding clock pairs) and the latest adjustment,
/// if any - the audit trail is always visible, never silently replaced.
///
/// <see cref="Reason"/> is required and validated server-side (AC04's own "exceptions require
/// audit reason") - never optional, never defaulted.
/// </summary>
public sealed record WorkforceAttendanceAdjustment
{
    public required string AdjustmentId { get; init; }
    public required string TenantId { get; init; }
    public required string PersonId { get; init; }
    public required string Date { get; init; }
    public required int OriginalOvertimeMinutes { get; init; }
    public required int AdjustedOvertimeMinutes { get; init; }
    public required string Reason { get; init; }
    public required string AdjustedByActorId { get; init; }
    public required string AdjustedAt { get; init; }

    public static WorkforceAttendanceAdjustment Create(
        string tenantId, string personId, string date, int originalOvertimeMinutes, int adjustedOvertimeMinutes,
        string reason, string actorId, string now)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 500)
        {
            throw WorkforceAttendanceError.Validation([new Api.ValidationErrorItem("reason", "invalid", "reason must be non-empty and at most 500 characters")]);
        }

        if (adjustedOvertimeMinutes < 0)
        {
            throw WorkforceAttendanceError.Validation([new Api.ValidationErrorItem("adjusted_overtime_minutes", "out_of_range", "adjusted_overtime_minutes must not be negative")]);
        }

        return new WorkforceAttendanceAdjustment
        {
            AdjustmentId = CanonicalId.NewEventId(),
            TenantId = tenantId,
            PersonId = personId,
            Date = date,
            OriginalOvertimeMinutes = originalOvertimeMinutes,
            AdjustedOvertimeMinutes = adjustedOvertimeMinutes,
            Reason = reason,
            AdjustedByActorId = actorId,
            AdjustedAt = now,
        };
    }
}

using Biolog.Features.Attendance.Identifiers;

namespace Biolog.Features.Attendance.Domain;

/// <summary>
/// AC04 - one tenant policy (natural key <c>TenantId</c> alone: exactly one policy per tenant,
/// mirroring "no new registered id type for a singleton tenant-scoped config record"). A
/// mutable, CAS-versioned tenant aggregate (ADR-0001 §3.1/§3.2 - the same optimistic-concurrency
/// shape <c>BoothCapture</c>/<c>AccessPolicy</c> use), because a policy is a live configuration
/// setting a tenant administrator may revise, not an immutable fact like
/// <see cref="AttendanceEvent"/>.
///
/// <see cref="AttendanceStatusEvaluator"/> is the only reader; nothing here ever reaches back
/// into a stored <see cref="AttendanceEvent"/> or mutates one - AC04's "tenant policies define
/// interpretation" is a pure function of (events, policy), never a write to either input.
/// </summary>
public sealed class AttendancePolicy
{
    public string TenantId { get; }
    public AttendanceInterpretationMode InterpretationMode { get; }

    /// <summary>"HH:mm" 24-hour, tenant-local wall time with no timezone attached (this story does not model tenant timezones - a disclosed simplification; every comparison in <see cref="AttendanceStatusEvaluator"/> works in the same convention as <see cref="AttendanceEvent.OccurredAt"/>'s own UTC clock). <c>null</c> means "no expected start time configured" - every <see cref="AttendanceMode.CheckIn"/> that day is <see cref="AttendanceStatusValue.Present"/>, never <see cref="AttendanceStatusValue.Late"/>, since lateness cannot be judged against nothing.</summary>
    public string? ExpectedStartTime { get; }

    public int LateGraceMinutes { get; }
    public int Version { get; }
    public string UpdatedAt { get; }
    public string UpdatedBy { get; }

    private AttendancePolicy(string tenantId, AttendanceInterpretationMode interpretationMode, string? expectedStartTime, int lateGraceMinutes, int version, string updatedAt, string updatedBy)
    {
        TenantId = tenantId;
        InterpretationMode = interpretationMode;
        ExpectedStartTime = expectedStartTime;
        LateGraceMinutes = lateGraceMinutes;
        Version = version;
        UpdatedAt = updatedAt;
        UpdatedBy = updatedBy;
    }

    /// <summary>The policy every tenant has implicitly until an administrator sets one explicitly - <see cref="AttendanceInterpretationMode.Presence"/>, no expected start time, so any <see cref="AttendanceMode.CheckIn"/> that day is simply <see cref="AttendanceStatusValue.Present"/>. Version 0 so the first real <see cref="Update"/> is distinguishable from a genuine prior version.</summary>
    public static AttendancePolicy Default(string tenantId) =>
        new(tenantId, AttendanceInterpretationMode.Presence, expectedStartTime: null, lateGraceMinutes: 0, version: 0, updatedAt: UtcInstant.Render(DateTime.UnixEpoch), updatedBy: "system");

    public static AttendancePolicy Create(string tenantId, AttendanceInterpretationMode interpretationMode, string? expectedStartTime, int lateGraceMinutes, string actorId, string now)
    {
        ValidateFields(expectedStartTime, lateGraceMinutes);
        return new AttendancePolicy(tenantId, interpretationMode, expectedStartTime, lateGraceMinutes, version: 1, updatedAt: now, updatedBy: actorId);
    }

    /// <summary>Returns a NEW instance (never mutates this one) one version ahead - the caller (<see cref="AttendancePolicyService"/>) is responsible for the compare-and-set write against <see cref="Version"/>.</summary>
    public AttendancePolicy Update(AttendanceInterpretationMode interpretationMode, string? expectedStartTime, int lateGraceMinutes, string actorId, string now)
    {
        ValidateFields(expectedStartTime, lateGraceMinutes);
        return new AttendancePolicy(TenantId, interpretationMode, expectedStartTime, lateGraceMinutes, Version + 1, now, actorId);
    }

    private static void ValidateFields(string? expectedStartTime, int lateGraceMinutes)
    {
        if (expectedStartTime is not null && !TimeOnly.TryParseExact(expectedStartTime, "HH:mm", out _))
        {
            throw AttendanceError.Validation([new Api.ValidationErrorItem("expected_start_time", "invalid_format", "expected_start_time must be 'HH:mm' 24-hour or null")]);
        }

        if (lateGraceMinutes < 0 || lateGraceMinutes > 24 * 60)
        {
            throw AttendanceError.Validation([new Api.ValidationErrorItem("late_grace_minutes", "out_of_range", "late_grace_minutes must be between 0 and 1440")]);
        }
    }
}

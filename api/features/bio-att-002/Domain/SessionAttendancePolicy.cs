namespace Biolog.Features.AttendanceSessions.Domain;

/// <summary>
/// AC02 - one policy per <c>(TenantId, SessionType)</c> (natural key), a mutable, CAS-versioned
/// aggregate (ADR-0001 §3.1/§3.2 - the same shape BIO-ATT-001's own <c>AttendancePolicy</c> uses),
/// because a policy is a live configuration setting a tenant administrator may revise.
///
/// <b>Genuinely richer granularity than BIO-ATT-001's own <c>AttendancePolicy</c>.</b> That story
/// has exactly ONE policy per tenant (a single <c>LateGraceMinutes</c> threshold shared by every
/// capture, regardless of what kind of occasion it was for). AC02's own wording - "present/late/
/// absent rules are configurable" - is satisfied here by keying the policy on <see cref="SessionType"/>
/// as well as tenant: an exam and a hostel roll-call can have different lateness/absence
/// thresholds for the same tenant. <see cref="SessionAttendanceClassifier"/> is the only reader;
/// nothing here ever reaches back into a stored BIO-ATT-001 <c>AttendanceEvent</c> or a
/// <see cref="Session"/>, or mutates either - AC02's "rules are configurable" is a pure function of
/// (evidence, policy), never a write to either input.
/// </summary>
public sealed class SessionAttendancePolicy
{
    public string TenantId { get; }
    public SessionType SessionType { get; }

    /// <summary>Minutes after <see cref="Session.ScheduledStartTime"/> within which a first arrival still counts as <see cref="SessionAttendanceStatusValue.Present"/>.</summary>
    public int LateGraceMinutes { get; }

    /// <summary>Minutes after <see cref="Session.ScheduledStartTime"/> beyond which a first arrival (or no arrival at all) counts as <see cref="SessionAttendanceStatusValue.Absent"/> rather than <see cref="SessionAttendanceStatusValue.Late"/>. Must be <c>&gt;= <see cref="LateGraceMinutes"/></c>.</summary>
    public int AbsentAfterMinutes { get; }

    public int Version { get; }
    public string UpdatedAt { get; }
    public string UpdatedBy { get; }

    private SessionAttendancePolicy(string tenantId, SessionType sessionType, int lateGraceMinutes, int absentAfterMinutes, int version, string updatedAt, string updatedBy)
    {
        TenantId = tenantId;
        SessionType = sessionType;
        LateGraceMinutes = lateGraceMinutes;
        AbsentAfterMinutes = absentAfterMinutes;
        Version = version;
        UpdatedAt = updatedAt;
        UpdatedBy = updatedBy;
    }

    /// <summary>The policy every (tenant, session type) pair has implicitly until an administrator sets one explicitly - 15 minutes' grace, absent beyond 30. Version 0 so the first real <see cref="Update"/> is distinguishable from a genuine prior version, the same convention BIO-ATT-001's own <c>AttendancePolicy.Default</c> uses.</summary>
    public static SessionAttendancePolicy Default(string tenantId, SessionType sessionType) =>
        new(tenantId, sessionType, lateGraceMinutes: 15, absentAfterMinutes: 30, version: 0, updatedAt: Identifiers.UtcInstant.Render(DateTime.UnixEpoch), updatedBy: "system");

    public static SessionAttendancePolicy Create(string tenantId, SessionType sessionType, int lateGraceMinutes, int absentAfterMinutes, string actorId, string now)
    {
        ValidateFields(lateGraceMinutes, absentAfterMinutes);
        return new SessionAttendancePolicy(tenantId, sessionType, lateGraceMinutes, absentAfterMinutes, version: 1, updatedAt: now, updatedBy: actorId);
    }

    /// <summary>Returns a NEW instance (never mutates this one), one version ahead - the caller (<see cref="SessionAttendancePolicyService"/>) is responsible for the compare-and-set write against <see cref="Version"/>.</summary>
    public SessionAttendancePolicy Update(int lateGraceMinutes, int absentAfterMinutes, string actorId, string now)
    {
        ValidateFields(lateGraceMinutes, absentAfterMinutes);
        return new SessionAttendancePolicy(TenantId, SessionType, lateGraceMinutes, absentAfterMinutes, Version + 1, now, actorId);
    }

    private static void ValidateFields(int lateGraceMinutes, int absentAfterMinutes)
    {
        if (lateGraceMinutes < 0 || lateGraceMinutes > 24 * 60)
        {
            throw SessionError.Validation([new Api.ValidationErrorItem("late_grace_minutes", "out_of_range", "late_grace_minutes must be between 0 and 1440")]);
        }

        if (absentAfterMinutes < lateGraceMinutes || absentAfterMinutes > 24 * 60)
        {
            throw SessionError.Validation([new Api.ValidationErrorItem("absent_after_minutes", "out_of_range", "absent_after_minutes must be >= late_grace_minutes and at most 1440")]);
        }
    }
}

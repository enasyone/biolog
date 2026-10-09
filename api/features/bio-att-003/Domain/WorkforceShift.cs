using Biolog.Features.WorkforceAttendance.Identifiers;

namespace Biolog.Features.WorkforceAttendance.Domain;

/// <summary>
/// AC01 - one named shift schedule a tenant administrator configures: a start/end time and its
/// own grace period. Unlike BIO-ATT-001's own <see cref="AttendancePolicy"/> (exactly one
/// tenant-wide policy), a tenant may configure MANY shifts (e.g. "Morning", "Night") - this
/// story's own AC01 wording is plural ("shift schedules"), a genuinely different shape from
/// BIO-ATT-001's singleton. A mutable, CAS-versioned aggregate (ADR-0001 §3.1/§3.2), because a
/// shift is a live configuration setting an administrator may revise, not an immutable fact.
///
/// <see cref="OvertimeEvaluator"/> is the only reader of a shift's own duration; nothing here
/// ever reaches into a raw clock event or mutates one (AC03's own "derived, not raw-event
/// mutation").
/// </summary>
public sealed record WorkforceShift
{
    public required string ShiftId { get; init; }
    public required string TenantId { get; init; }
    public required string Name { get; init; }

    /// <summary>"HH:mm" 24-hour, tenant-local wall time with no timezone attached - the same disclosed simplification BIO-ATT-001's own <c>AttendancePolicy.ExpectedStartTime</c> already carries.</summary>
    public required string StartTime { get; init; }

    /// <summary>"HH:mm" 24-hour. May be numerically before <see cref="StartTime"/> to represent an overnight shift (e.g. 22:00-06:00) - <see cref="ExpectedDurationMinutes"/> accounts for the wrap.</summary>
    public required string EndTime { get; init; }

    public required int GraceMinutes { get; init; }
    public required int Version { get; init; }
    public required string CreatedAt { get; init; }
    public required string UpdatedAt { get; init; }
    public required string UpdatedBy { get; init; }

    /// <summary>The shift's own expected worked duration, wrapping past midnight when <see cref="EndTime"/> is numerically before <see cref="StartTime"/> - the input <see cref="OvertimeEvaluator"/> compares a day's actual worked minutes against.</summary>
    public int ExpectedDurationMinutes
    {
        get
        {
            var start = TimeOnly.ParseExact(StartTime, "HH:mm");
            var end = TimeOnly.ParseExact(EndTime, "HH:mm");
            var minutes = (end.ToTimeSpan() - start.ToTimeSpan()).TotalMinutes;
            return (int)(minutes >= 0 ? minutes : minutes + (24 * 60));
        }
    }

    public static WorkforceShift Create(string tenantId, string name, string startTime, string endTime, int graceMinutes, string actorId, string now)
    {
        ValidateFields(name, startTime, endTime, graceMinutes);
        return new WorkforceShift
        {
            ShiftId = CanonicalId.NewEventId(),
            TenantId = tenantId,
            Name = name,
            StartTime = startTime,
            EndTime = endTime,
            GraceMinutes = graceMinutes,
            Version = 1,
            CreatedAt = now,
            UpdatedAt = now,
            UpdatedBy = actorId,
        };
    }

    /// <summary>Returns a NEW instance (never mutates this one) one version ahead - the caller (<see cref="WorkforceShiftService"/>) is responsible for the compare-and-set write against <see cref="Version"/>.</summary>
    public WorkforceShift Update(string name, string startTime, string endTime, int graceMinutes, string actorId, string now)
    {
        ValidateFields(name, startTime, endTime, graceMinutes);
        return this with { Name = name, StartTime = startTime, EndTime = endTime, GraceMinutes = graceMinutes, Version = Version + 1, UpdatedAt = now, UpdatedBy = actorId };
    }

    private static void ValidateFields(string name, string startTime, string endTime, int graceMinutes)
    {
        var errors = new List<Api.ValidationErrorItem>();

        if (string.IsNullOrWhiteSpace(name) || name.Length > 100)
        {
            errors.Add(new Api.ValidationErrorItem("name", "invalid", "name must be non-empty and at most 100 characters"));
        }

        if (!TimeOnly.TryParseExact(startTime, "HH:mm", out _))
        {
            errors.Add(new Api.ValidationErrorItem("start_time", "invalid_format", "start_time must be 'HH:mm' 24-hour"));
        }

        if (!TimeOnly.TryParseExact(endTime, "HH:mm", out _))
        {
            errors.Add(new Api.ValidationErrorItem("end_time", "invalid_format", "end_time must be 'HH:mm' 24-hour"));
        }

        if (graceMinutes < 0 || graceMinutes > 24 * 60)
        {
            errors.Add(new Api.ValidationErrorItem("grace_minutes", "out_of_range", "grace_minutes must be between 0 and 1440"));
        }

        if (errors.Count > 0)
        {
            throw WorkforceAttendanceError.Validation(errors);
        }
    }
}

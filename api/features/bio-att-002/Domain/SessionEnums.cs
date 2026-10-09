namespace Biolog.Features.AttendanceSessions.Domain;

/// <summary>AC01's "lecture/class/exam/hostel/event" vocabulary (the user story's own words) - also the granularity key for AC02's configurable <see cref="SessionAttendancePolicy"/>.</summary>
public enum SessionType
{
    Lecture,
    Class,
    Exam,
    Hostel,
    Event,
}

public static class SessionTypeExtensions
{
    public static string ToWireString(this SessionType type) => type switch
    {
        SessionType.Lecture => "lecture",
        SessionType.Class => "class",
        SessionType.Exam => "exam",
        SessionType.Hostel => "hostel",
        SessionType.Event => "event",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "unreachable: exhaustive switch"),
    };

    public static bool TryParse(string? value, out SessionType type)
    {
        switch (value)
        {
            case "lecture": type = SessionType.Lecture; return true;
            case "class": type = SessionType.Class; return true;
            case "exam": type = SessionType.Exam; return true;
            case "hostel": type = SessionType.Hostel; return true;
            case "event": type = SessionType.Event; return true;
            default: type = default; return false;
        }
    }
}

/// <summary>
/// A derived (never raw, never persisted as a mutation of anything) per-roster-member
/// classification for one session, computed at read time by
/// <see cref="SessionAttendanceClassifier"/> from BIO-ATT-001's own raw
/// <c>AttendanceEvent</c>s plus this feature's own <see cref="SessionAttendancePolicy"/>. A
/// distinct type from BIO-ATT-001's own <c>AttendanceStatusValue</c> - this feature computes its
/// own classification against its own session-scoped policy, it does not reuse or reinterpret
/// BIO-ATT-001's tenant-wide one.
/// </summary>
public enum SessionAttendanceStatusValue
{
    Present,
    Late,
    Absent,
}

public static class SessionAttendanceStatusValueExtensions
{
    public static string ToWireString(this SessionAttendanceStatusValue value) => value switch
    {
        SessionAttendanceStatusValue.Present => "present",
        SessionAttendanceStatusValue.Late => "late",
        SessionAttendanceStatusValue.Absent => "absent",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "unreachable: exhaustive switch"),
    };

    public static bool TryParse(string? value, out SessionAttendanceStatusValue status)
    {
        switch (value)
        {
            case "present": status = SessionAttendanceStatusValue.Present; return true;
            case "late": status = SessionAttendanceStatusValue.Late; return true;
            case "absent": status = SessionAttendanceStatusValue.Absent; return true;
            default: status = default; return false;
        }
    }
}

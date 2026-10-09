namespace Biolog.Features.Attendance.Domain;

/// <summary>
/// How an event's occurrence was learned of (AC01 "source/device"). <see cref="Device"/> is the
/// expected common case (a reader/kiosk/terminal tap); <see cref="Manual"/> covers an
/// administrator recording an event with no device involved (e.g. a paper-register catch-up);
/// <see cref="Import"/> covers a bulk historical load. Only <see cref="Device"/> requires
/// <see cref="AttendanceEvent.DeviceId"/> to be present.
/// </summary>
public enum AttendanceSource
{
    Device,
    Manual,
    Import,
}

public static class AttendanceSourceExtensions
{
    public static string ToWireString(this AttendanceSource source) => source switch
    {
        AttendanceSource.Device => "device",
        AttendanceSource.Manual => "manual",
        AttendanceSource.Import => "import",
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, "unreachable: exhaustive switch"),
    };

    public static bool TryParse(string? value, out AttendanceSource source)
    {
        switch (value)
        {
            case "device": source = AttendanceSource.Device; return true;
            case "manual": source = AttendanceSource.Manual; return true;
            case "import": source = AttendanceSource.Import; return true;
            default: source = default; return false;
        }
    }
}

/// <summary>
/// The kind of physical/logical occurrence a raw event represents (AC01 "mode"). Deliberately
/// small: MASTER-TECHNICAL-SPECIFICATION.md §10 names "education, workforce and presence
/// interpretations" as a derived-status concern (AC02/AC04), not a raw-event vocabulary - the
/// raw event only records what physically happened, never which interpretation applies.
/// </summary>
public enum AttendanceMode
{
    CheckIn,
    CheckOut,
    Presence,
}

public static class AttendanceModeExtensions
{
    public static string ToWireString(this AttendanceMode mode) => mode switch
    {
        AttendanceMode.CheckIn => "check_in",
        AttendanceMode.CheckOut => "check_out",
        AttendanceMode.Presence => "presence",
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "unreachable: exhaustive switch"),
    };

    public static bool TryParse(string? value, out AttendanceMode mode)
    {
        switch (value)
        {
            case "check_in": mode = AttendanceMode.CheckIn; return true;
            case "check_out": mode = AttendanceMode.CheckOut; return true;
            case "presence": mode = AttendanceMode.Presence; return true;
            default: mode = default; return false;
        }
    }
}

/// <summary>How the event's claimed identity was verified (AC01 "confidence/verification evidence"). <see cref="Unverified"/> is a real, honest value - not every capture path can verify (e.g. a manual register entry) - never a default masking a missing check.</summary>
public enum VerificationMethod
{
    Biometric,
    Credential,
    Manual,
    Unverified,
}

public static class VerificationMethodExtensions
{
    public static string ToWireString(this VerificationMethod method) => method switch
    {
        VerificationMethod.Biometric => "biometric",
        VerificationMethod.Credential => "credential",
        VerificationMethod.Manual => "manual",
        VerificationMethod.Unverified => "unverified",
        _ => throw new ArgumentOutOfRangeException(nameof(method), method, "unreachable: exhaustive switch"),
    };

    public static bool TryParse(string? value, out VerificationMethod method)
    {
        switch (value)
        {
            case "biometric": method = VerificationMethod.Biometric; return true;
            case "credential": method = VerificationMethod.Credential; return true;
            case "manual": method = VerificationMethod.Manual; return true;
            case "unverified": method = VerificationMethod.Unverified; return true;
            default: method = default; return false;
        }
    }
}

/// <summary>Tenant-wide interpretation profile (AC04; MASTER-TECHNICAL-SPECIFICATION.md §10: "education, workforce and presence interpretations"). Governs only how <see cref="AttendanceStatusEvaluator"/> reads raw events - never stored on the event itself (AC02).</summary>
public enum AttendanceInterpretationMode
{
    Education,
    Workforce,
    Presence,
}

public static class AttendanceInterpretationModeExtensions
{
    public static string ToWireString(this AttendanceInterpretationMode mode) => mode switch
    {
        AttendanceInterpretationMode.Education => "education",
        AttendanceInterpretationMode.Workforce => "workforce",
        AttendanceInterpretationMode.Presence => "presence",
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "unreachable: exhaustive switch"),
    };

    public static bool TryParse(string? value, out AttendanceInterpretationMode mode)
    {
        switch (value)
        {
            case "education": mode = AttendanceInterpretationMode.Education; return true;
            case "workforce": mode = AttendanceInterpretationMode.Workforce; return true;
            case "presence": mode = AttendanceInterpretationMode.Presence; return true;
            default: mode = default; return false;
        }
    }
}

/// <summary>A derived (AC02: never raw) daily attendance classification, computed at read time by <see cref="AttendanceStatusEvaluator"/> - never persisted as a mutation of any <see cref="AttendanceEvent"/>.</summary>
public enum AttendanceStatusValue
{
    Present,
    Late,
    Absent,
}

public static class AttendanceStatusValueExtensions
{
    public static string ToWireString(this AttendanceStatusValue value) => value switch
    {
        AttendanceStatusValue.Present => "present",
        AttendanceStatusValue.Late => "late",
        AttendanceStatusValue.Absent => "absent",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "unreachable: exhaustive switch"),
    };
}

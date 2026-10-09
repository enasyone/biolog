namespace Biolog.Features.WorkforceAttendance.Domain;

/// <summary>
/// This feature's own mirror of BIO-ATT-001's <c>AttendanceMode</c> wire vocabulary
/// (<c>check_in</c>/<c>check_out</c>/<c>presence</c>) - a separate type, not a shared one,
/// because this project has no <c>ProjectReference</c> to BIO-ATT-001's own assembly
/// (see this project's own csproj header). <see cref="IAttendanceEventGateway"/>'s host adapter
/// maps BIO-ATT-001's own enum values onto these wire strings one-for-one; nothing here invents
/// a new vocabulary.
/// </summary>
public enum ClockMode
{
    CheckIn,
    CheckOut,
    Presence,
}

public static class ClockModeExtensions
{
    public static bool TryParse(string? value, out ClockMode mode)
    {
        switch (value)
        {
            case "check_in": mode = ClockMode.CheckIn; return true;
            case "check_out": mode = ClockMode.CheckOut; return true;
            case "presence": mode = ClockMode.Presence; return true;
            default: mode = default; return false;
        }
    }
}

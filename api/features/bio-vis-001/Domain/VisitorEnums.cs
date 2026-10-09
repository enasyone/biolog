namespace Biolog.Features.Visitors.Domain;

/// <summary>Host approval workflow state (AC02 - "host approval states are auditable"). Every transition out of <see cref="Pending"/> is terminal - there is no re-deciding an already-decided request (mirrors BIO-IDN-006's "no re-consuming an already-consumed approval" discipline).</summary>
public enum HostApprovalStatus
{
    Pending,
    Approved,
    Denied,
}

public static class HostApprovalStatusExtensions
{
    public static string ToWireString(this HostApprovalStatus status) => status switch
    {
        HostApprovalStatus.Pending => "pending",
        HostApprovalStatus.Approved => "approved",
        HostApprovalStatus.Denied => "denied",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "unreachable: exhaustive switch"),
    };
}

/// <summary>
/// The visit's own persisted lifecycle state (distinct from <see cref="HostApprovalStatus"/> -
/// a visitor can be <see cref="Approved"/> for days before ever arriving). Deliberately has NO
/// <c>Expired</c> member - expiry is never written to this field (see
/// <see cref="EffectiveVisitStatus"/>/<see cref="VisitorStatusEvaluator"/>'s own header for why).
/// </summary>
public enum VisitStatus
{
    Preregistered,
    CheckedIn,
    CheckedOut,
}

public static class VisitStatusExtensions
{
    public static string ToWireString(this VisitStatus status) => status switch
    {
        VisitStatus.Preregistered => "preregistered",
        VisitStatus.CheckedIn => "checked-in",
        VisitStatus.CheckedOut => "checked-out",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "unreachable: exhaustive switch"),
    };
}

/// <summary>
/// AC04 - a derived (never persisted) read-time overlay of <see cref="VisitStatus"/>: the SAME
/// discipline BIO-IDN-002 established for time-based status ("refuse auto-flip ... compute in
/// effect at read time") and BIO-ATT-001 already applied for its own derived status. A genuinely
/// separate enum type from <see cref="VisitStatus"/> (not the same type reused for double duty),
/// mirroring <c>AttendanceMode</c> (raw) vs. <c>AttendanceStatusValue</c> (derived) being distinct
/// types - so a reviewer can see at the type level, not just by convention, that <c>Expired</c>
/// can never be written to a stored <see cref="VisitorRecord.VisitStatus"/>.
/// </summary>
public enum EffectiveVisitStatus
{
    Preregistered,
    CheckedIn,
    CheckedOut,
    Expired,
}

public static class EffectiveVisitStatusExtensions
{
    public static string ToWireString(this EffectiveVisitStatus status) => status switch
    {
        EffectiveVisitStatus.Preregistered => "preregistered",
        EffectiveVisitStatus.CheckedIn => "checked-in",
        EffectiveVisitStatus.CheckedOut => "checked-out",
        EffectiveVisitStatus.Expired => "expired",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "unreachable: exhaustive switch"),
    };
}

/// <summary>
/// AC03 "watchlist ... options" - caller-supplied screening evidence only (this feature has no
/// declared dependency on a watchlist-source integration, so it never performs a screening
/// itself), the same "disclosed, not silently absorbed" boundary BIO-ACC-001 recorded for
/// caller-supplied evaluation inputs it does not resolve, and BIO-ATT-001 applied to
/// <c>device_id</c>.
/// </summary>
public enum WatchlistScreeningResult
{
    NotScreened,
    Clear,
    Flagged,
}

public static class WatchlistScreeningResultExtensions
{
    public static string ToWireString(this WatchlistScreeningResult result) => result switch
    {
        WatchlistScreeningResult.NotScreened => "not_screened",
        WatchlistScreeningResult.Clear => "clear",
        WatchlistScreeningResult.Flagged => "flagged",
        _ => throw new ArgumentOutOfRangeException(nameof(result), result, "unreachable: exhaustive switch"),
    };

    public static bool TryParse(string? value, out WatchlistScreeningResult result)
    {
        switch (value)
        {
            case "not_screened": result = WatchlistScreeningResult.NotScreened; return true;
            case "clear": result = WatchlistScreeningResult.Clear; return true;
            case "flagged": result = WatchlistScreeningResult.Flagged; return true;
            default: result = default; return false;
        }
    }
}

namespace Biolog.Features.VisitorCredentials.Domain;

/// <summary>
/// AC04 - the one place a <see cref="VisitorRecordView"/> becomes a <see cref="VisitorEffectiveStatus"/>.
/// A pure function: same (view, now) in, same result out, every time - no clock read of its own,
/// no I/O, no mutation, and it never touches BIO-VIS-001's own stored record. Mirrors
/// BIO-VIS-001's own <c>VisitorStatusEvaluator</c> exactly, distinguishing the one case that
/// evaluator collapses: <see cref="VisitorEffectiveStatus.Overstay"/> (was checked in, now past
/// expiry, never checked out) from <see cref="VisitorEffectiveStatus.Expired"/> (never arrived,
/// preregistration lapsed).
/// </summary>
public static class VisitorOverstayEvaluator
{
    public static VisitorEffectiveStatus EffectiveStatus(VisitorRecordView view, string now)
    {
        if (view.VisitStatusWire == "checked-out")
        {
            return VisitorEffectiveStatus.CheckedOut; // a departed visitor's record never "expires" retroactively
        }

        var pastExpiry = string.CompareOrdinal(now, view.ExpiresAt) > 0; // RFC 3339 UTC sorts lexicographically == chronologically

        return view.VisitStatusWire switch
        {
            "checked-in" => pastExpiry ? VisitorEffectiveStatus.Overstay : VisitorEffectiveStatus.CheckedIn,
            "preregistered" => pastExpiry ? VisitorEffectiveStatus.Expired : VisitorEffectiveStatus.Preregistered,
            _ => throw new ArgumentOutOfRangeException(nameof(view), view.VisitStatusWire, "unreachable: BIO-VIS-001's own VisitStatus has exactly these three wire values"),
        };
    }
}

namespace Biolog.Features.Visitors.Domain;

/// <summary>
/// AC04 - the one place a stored <see cref="VisitorRecord"/> becomes an
/// <see cref="EffectiveVisitStatus"/>. A pure function: same (record, now) in, same result out,
/// every time - no clock read of its own, no I/O, no mutation. Mirrors BIO-ATT-001's own
/// <c>AttendanceStatusEvaluator</c> exactly, applied to expiry instead of lateness.
/// </summary>
public static class VisitorStatusEvaluator
{
    public static EffectiveVisitStatus EffectiveStatus(VisitorRecord record, string now)
    {
        if (record.VisitStatus == VisitStatus.CheckedOut)
        {
            return EffectiveVisitStatus.CheckedOut; // a departed visitor's record never "expires" retroactively
        }

        if (string.CompareOrdinal(now, record.ExpiresAt) > 0) // RFC 3339 UTC sorts lexicographically == chronologically
        {
            return EffectiveVisitStatus.Expired;
        }

        return record.VisitStatus switch
        {
            VisitStatus.Preregistered => EffectiveVisitStatus.Preregistered,
            VisitStatus.CheckedIn => EffectiveVisitStatus.CheckedIn,
            _ => throw new ArgumentOutOfRangeException(nameof(record), record.VisitStatus, "unreachable: CheckedOut handled above"),
        };
    }
}

namespace Biolog.Features.OperationalDashboard.Domain;

/// <summary>
/// BIO-RPT-004-AC03: every panel in <see cref="OperationalDashboardSnapshot"/> carries one of
/// these explicitly, plus its own <c>ObservedAt</c> timestamp - never left for a client to infer
/// from a number's shape alone (Seyi round-1 finding BIO-RPT-004-F02: the two states this enum
/// used to conflate, "a real source that is currently degraded" and "no source exists at all,"
/// are semantically different and must look different).
///
/// <see cref="Live"/>: the owning gateway's read just succeeded. <see cref="Stale"/>: a real,
/// governed data source exists for this domain (Identity/Access/Production), but the most recent
/// read attempt FAILED (the gateway threw) - <see cref="DashboardService"/> catches that
/// per-panel, so one failing source degrades only its own panel to Stale rather than crashing the
/// whole snapshot (the "offline signal" F02 asked for). <see cref="Unavailable"/>: no tenant-wide
/// read exists anywhere in the owning feature for this domain at all (Card/Device/Sync) - a
/// structurally different, permanent fact, not a transient failure, and never relabelled "Stale"
/// anywhere in this feature (UI tone is also visibly distinct per state - see
/// <c>OperationalHealthPanel.tsx</c>'s own <c>FreshnessBadge</c>).
/// </summary>
public enum DataFreshness
{
    Live,
    Stale,
    Unavailable,
}

public static class DataFreshnessExtensions
{
    public static string ToWireString(this DataFreshness freshness) => freshness switch
    {
        DataFreshness.Live => "live",
        DataFreshness.Stale => "stale",
        DataFreshness.Unavailable => "unavailable",
        _ => throw new ArgumentOutOfRangeException(nameof(freshness), freshness, "unreachable: exhaustive switch"),
    };
}

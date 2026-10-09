namespace Biolog.Features.OperationalDashboard.Domain;

/// <summary>
/// BIO-RPT-004-AC01/AC03: a tenant-scoped PERSON count. <see cref="Freshness"/> is
/// <see cref="DataFreshness.Live"/> when the read just succeeded (counts are this request's own
/// fresh values, <see cref="ObservedAt"/> is now). <see cref="DataFreshness.Stale"/> means the
/// read FAILED - counts are the last SUCCESSFULLY observed values (<see cref="ObservedAt"/> is
/// THEIR true observation time, not the failure's own time), retained via
/// <see cref="LastKnownHealthCache"/>. If no successful read has EVER happened for this tenant,
/// counts are all <c>null</c> (never a fabricated zero - Seyi round-2 F04) and
/// <see cref="ObservedAt"/> is the failed attempt's own time (there is no earlier observation to
/// report).
/// </summary>
public sealed record IdentityPanel(int? Total, int? Active, int? Suspended, int? Other, DataFreshness Freshness, string ObservedAt, string? Reason = null);

/// <summary>BIO-RPT-004-AC01/AC03: a tenant-scoped access-decision count. Same Live/Stale/last-known shape as <see cref="IdentityPanel"/>.</summary>
public sealed record AccessPanel(int? Total, int? Allowed, int? Denied, DataFreshness Freshness, string ObservedAt, string? Reason = null);

/// <summary>BIO-RPT-004-AC01/AC03: a tenant-scoped production-job count. Same Live/Stale/last-known shape as <see cref="IdentityPanel"/>.</summary>
public sealed record ProductionPanel(int? Total, int? Passed, int? Failed, int? Rejected, int? InProgress, int? Cancelled, DataFreshness Freshness, string ObservedAt, string? Reason = null);

/// <summary>
/// BIO-RPT-004-AC03/AC05: the Card/Device/Sync panels. Each of their three owning features
/// (BIO-CRD-005/006, BIO-DEV-001, BIO-MOB-003 respectively) was verified directly - not assumed -
/// to expose no tenant-wide read today (see <c>Biolog.Features.OperationalDashboard.csproj</c>'s
/// own header for the specific method each is missing). Rather than fabricate a plausible-looking
/// number against a real gap, this record always carries <see cref="DataFreshness.Unavailable"/>
/// plus a <see cref="Reason"/> a caller/UI can render verbatim - the honest form of BIO-RPT-004-
/// AC05's "explicitly documented HYBRID state" allowance, not a silent placeholder, and NEVER the
/// same wire value or UI tone as <see cref="DataFreshness.Stale"/> (Seyi round-1 F02: "unavailable
/// data is not stale data").
/// </summary>
public sealed record UnavailablePanel(DataFreshness Freshness, string Reason, string ObservedAt)
{
    public static UnavailablePanel For(string reason, string observedAt) => new(DataFreshness.Unavailable, reason, observedAt);
}

/// <summary>
/// BIO-RPT-004-AC01's whole response: one snapshot per request, covering all six domains the
/// user story names (identity, card, device, sync, access, production state). Never carries more
/// than one tenant's data (AC04) - <see cref="TenantId"/> is always the one
/// <see cref="DashboardService"/> itself resolved server-side, never a caller-supplied value, and
/// every panel inside is built from a gateway call scoped to that same tenant id - there is no
/// code path anywhere in this feature that can read a second tenant's data into one response.
/// Reaching this snapshot at all additionally requires the requesting actor to hold
/// <see cref="DashboardPermissionKeys.ViewOperationalHealth"/> (BIO-RPT-004-F01) - tenant scoping
/// alone is not authorization.
/// </summary>
public sealed record OperationalDashboardSnapshot
{
    public required string TenantId { get; init; }
    public required string ComputedAt { get; init; }
    public required IdentityPanel Identity { get; init; }
    public required UnavailablePanel Card { get; init; }
    public required UnavailablePanel Device { get; init; }
    public required UnavailablePanel Sync { get; init; }
    public required AccessPanel Access { get; init; }
    public required ProductionPanel Production { get; init; }
}

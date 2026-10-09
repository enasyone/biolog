using Biolog.Features.OperationalDashboard.Domain;
using Biolog.Features.OperationalDashboard.Identifiers;
using Biolog.Features.OperationalDashboard.Tenancy;

namespace Biolog.Features.OperationalDashboard;

/// <summary>
/// The application-facing surface this feature's one endpoint calls, the same role BIO-RPT-002's
/// own <c>ReportService</c> plays for its own feature. Resolves the current tenant itself
/// (BIO-RPT-004-AC01/AC04) rather than trusting a caller-supplied one - there is no overload
/// anywhere on this class that accepts a tenant id as a parameter, so no caller, malicious or
/// not, has any way to ask this service for a tenant other than the one the host channel resolved
/// for their own request.
///
/// <b>Permission-gated (Seyi round-1, BIO-RPT-004-F01).</b> Tenant scoping alone proved not to be
/// authorization: <see cref="GetSnapshotAsync"/> requires the caller to name itself
/// (<paramref name="actorId"/>, from <c>X-Actor-Id</c> - verified by <c>ActorProvenanceGate</c>
/// in <c>DashboardEndpoints</c> BEFORE this method is ever called, Seyi round-2 BIO-RPT-004-F03)
/// and checks <see cref="IDashboardPermissionGateway"/> BEFORE calling any health gateway - an
/// unprivileged actor is refused with <see cref="DashboardError.Forbidden"/> and never reaches a
/// read of identity/access/production data.
///
/// <b>Per-panel fault isolation with retained last-known values (Seyi round-1 F02, round-2 F04).</b>
/// Each of the three real gateway calls is wrapped individually: a thrown exception degrades ONLY
/// that panel to <see cref="DataFreshness.Stale"/> rather than crashing the whole snapshot. Round
/// 2 additionally caught that the Stale panel was a fabricated zero stamped with the failure's own
/// time - <see cref="LastKnownHealthCache"/> now records every successful read, and a failed read
/// reports those retained counts and their TRUE observation time instead; only a tenant that has
/// NEVER had a successful read reports no counts at all (never a zero).
/// </summary>
internal sealed class DashboardService(
    ICurrentTenantProvider tenantProvider,
    IDashboardPermissionGateway permissionGateway,
    IIdentityHealthGateway identityGateway,
    IAccessHealthGateway accessGateway,
    IProductionHealthGateway productionGateway,
    LastKnownHealthCache lastKnownCache)
{
    public async Task<OperationalDashboardSnapshot> GetSnapshotAsync(string actorId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();

        if (!await permissionGateway.HasPermissionAsync(actorId, DashboardPermissionKeys.ViewOperationalHealth, cancellationToken))
        {
            throw DashboardError.Forbidden();
        }

        var identity = await ReadIdentityAsync(tenantId, cancellationToken);
        var access = await ReadAccessAsync(tenantId, cancellationToken);
        var production = await ReadProductionAsync(tenantId, cancellationToken);

        var observedAt = UtcInstant.Now();
        return new OperationalDashboardSnapshot
        {
            TenantId = tenantId,
            ComputedAt = UtcInstant.Now(),
            Identity = identity,
            Access = access,
            Production = production,
            Card = UnavailablePanel.For(
                "No tenant-wide read exists yet over BIO-CRD-005/006's own card repositories - ICardIssueRepository/ICardLifecycleRepository expose no ListByTenant method. Disclosed HYBRID gap (BIO-RPT-004-AC05), not a fabricated number.",
                observedAt),
            Device = UnavailablePanel.For(
                "BIO-DEV-001's IDeviceRepository has no tenant-wide list method and Device.LastSeenAt is never populated by any existing code path. Disclosed HYBRID gap (BIO-RPT-004-AC05), not a fabricated number.",
                observedAt),
            Sync = UnavailablePanel.For(
                "BIO-MOB-003's offline-sync feature exposes no tenant-wide read of pending/failed mutations. Disclosed HYBRID gap (BIO-RPT-004-AC05), not a fabricated number.",
                observedAt),
        };
    }

    private async Task<IdentityPanel> ReadIdentityAsync(string tenantId, CancellationToken cancellationToken)
    {
        try
        {
            var counts = await identityGateway.GetAsync(tenantId, cancellationToken);
            var observedAt = UtcInstant.Now();
            lastKnownCache.RecordIdentity(tenantId, counts, observedAt);
            return new IdentityPanel(counts.Total, counts.Active, counts.Suspended, counts.Other, DataFreshness.Live, observedAt);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var reason = FailureReason(ex);
            var lastKnown = lastKnownCache.GetIdentity(tenantId);
            return lastKnown is { } known
                ? new IdentityPanel(known.Counts.Total, known.Counts.Active, known.Counts.Suspended, known.Counts.Other, DataFreshness.Stale, known.ObservedAt, LastKnownReason(reason, known.ObservedAt))
                : new IdentityPanel(null, null, null, null, DataFreshness.Stale, UtcInstant.Now(), NeverObservedReason(reason));
        }
    }

    private async Task<AccessPanel> ReadAccessAsync(string tenantId, CancellationToken cancellationToken)
    {
        try
        {
            var counts = await accessGateway.GetAsync(tenantId, cancellationToken);
            var observedAt = UtcInstant.Now();
            lastKnownCache.RecordAccess(tenantId, counts, observedAt);
            return new AccessPanel(counts.Total, counts.Allowed, counts.Denied, DataFreshness.Live, observedAt);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var reason = FailureReason(ex);
            var lastKnown = lastKnownCache.GetAccess(tenantId);
            return lastKnown is { } known
                ? new AccessPanel(known.Counts.Total, known.Counts.Allowed, known.Counts.Denied, DataFreshness.Stale, known.ObservedAt, LastKnownReason(reason, known.ObservedAt))
                : new AccessPanel(null, null, null, DataFreshness.Stale, UtcInstant.Now(), NeverObservedReason(reason));
        }
    }

    private async Task<ProductionPanel> ReadProductionAsync(string tenantId, CancellationToken cancellationToken)
    {
        try
        {
            var counts = await productionGateway.GetAsync(tenantId, cancellationToken);
            var observedAt = UtcInstant.Now();
            lastKnownCache.RecordProduction(tenantId, counts, observedAt);
            return new ProductionPanel(counts.Total, counts.Passed, counts.Failed, counts.Rejected, counts.InProgress, counts.Cancelled, DataFreshness.Live, observedAt);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var reason = FailureReason(ex);
            var lastKnown = lastKnownCache.GetProduction(tenantId);
            return lastKnown is { } known
                ? new ProductionPanel(known.Counts.Total, known.Counts.Passed, known.Counts.Failed, known.Counts.Rejected, known.Counts.InProgress, known.Counts.Cancelled, DataFreshness.Stale, known.ObservedAt, LastKnownReason(reason, known.ObservedAt))
                : new ProductionPanel(null, null, null, null, null, null, DataFreshness.Stale, UtcInstant.Now(), NeverObservedReason(reason));
        }
    }

    private static string FailureReason(Exception ex) => $"the underlying gateway read failed: {ex.Message}";

    private static string LastKnownReason(string failureReason, string lastObservedAt) =>
        $"{failureReason} - showing the last successfully observed values, as of {lastObservedAt} (not fabricated zeros).";

    private static string NeverObservedReason(string failureReason) =>
        $"{failureReason} - no successful read has ever been observed for this tenant, so no counts are reported (not a fabricated zero).";

    private string RequireTenant() => tenantProvider.TenantId ?? throw DashboardError.TenantContextMissing();
}

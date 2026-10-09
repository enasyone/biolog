namespace Biolog.Features.OperationalDashboard.Domain;

/// <summary>
/// The one genuine cross-story runtime dependency this story declares beyond its three health
/// gateways (BIO-IDN-006). Implemented by an adapter over
/// <c>Biolog.Features.Rbac.PermissionService.HasPermissionAsync</c> in <c>BioRpt004Module</c> -
/// never referenced directly, the same shape BIO-RPT-003's own
/// <c>IAuditExportPermissionGateway</c> already established for the identical dependency. No
/// tenant id parameter: the wrapped service resolves the SAME ambient tenant context this
/// feature's own request already established (<c>TenantContextMiddleware</c> runs once, before
/// either feature's services do).
///
/// Added in response to Seyi round-1 finding BIO-RPT-004-F01: tenant scoping alone ("this
/// request's own tenant") is not authorization ("this caller may view this tenant's dashboard") -
/// <see cref="DashboardService"/> now checks this BEFORE calling any health gateway, so an
/// unprivileged caller never even reaches a read of identity/access/production data.
/// </summary>
public interface IDashboardPermissionGateway
{
    Task<bool> HasPermissionAsync(string personId, string permissionKey, CancellationToken cancellationToken = default);
}

/// <summary>This story's own free-form, namespaced-verb permission key (BIO-IDN-006's own catalogue-less convention - this story does not own or enumerate every permission the whole system defines).</summary>
public static class DashboardPermissionKeys
{
    /// <summary>BIO-RPT-004-F01's gate: without this, GET /api/v1/dashboard/operational-health is refused with 403, regardless of how correctly the tenant itself was resolved.</summary>
    public const string ViewOperationalHealth = "dashboard.view_operational_health";
}

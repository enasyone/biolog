using Biolog.Features.AccessCache.Domain;

namespace Biolog.Features.AccessCache.Demo;

/// <summary>
/// AC06's own bridge: <c>web/features/access/live.ts</c>'s <c>accessViewAccessEventsLivePort</c>
/// already POSTs a bare, headerless request to <c>/api/v1/access/view-access-events</c> -
/// BIO-UX-010's own scaffolding, by design (no login/session story exists yet, the same
/// reasoning <c>AttendanceDemoBridgeService</c>/BIO-DOC-009's own
/// <c>AuthenticityEndpoints.ComputeAsync</c> already established for an identically-shaped
/// headerless UI caller). Requiring a tenant/actor on this one route would make it permanently
/// unreachable from the one UI AC06 exists to connect - so this bridge queries one FIXED,
/// already-public SYNTHETIC demo tenant (never a caller-supplied or ambient-resolved value),
/// genuinely reading this feature's own real <see cref="IPresentationEventLog"/> - a real,
/// connected live implementation of the mapped action, not a second mock.
///
/// In practice this demo tenant has no real presentation events (only a real, resolved-tenant
/// device can ever write one, via <see cref="Api.AccessCacheEndpoints"/>'s device-authenticated
/// routes) - reporting "0 access events on record" is still a genuine round trip through this
/// feature's own real backend code and real (currently empty) data, not a canned string; the
/// same honest-empty-result philosophy the button's outcome banner already models for every
/// other unconnected scenario.
///
/// Registered Singleton, depending only on the Singleton <see cref="IPresentationEventLog"/>
/// directly rather than the Scoped, tenant-bound <see cref="AccessCacheService"/> - the same
/// "no captive-dependency risk" reasoning <c>AttendanceDemoBridgeService</c>'s own header
/// documents: a header-less demo POST resolves no ambient tenant for a Scoped service to bind to.
///
/// <see cref="DemoTenantId"/> reuses the exact literal tenant id BIO-ATT-001's own
/// <c>AttendanceDemoBridgeService.DemoTenantId</c> already established - that schema's own
/// published canonical-id example (<c>contracts/schemas/identifier-registry.json</c>), shared
/// across every demo bridge rather than each minting its own arbitrary synthetic value.
/// </summary>
public sealed class AccessCacheDemoBridgeService(IPresentationEventLog eventLog)
{
    public const string DemoTenantId = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77";

    public async Task<string> ViewAccessEventsAsync(CancellationToken cancellationToken)
    {
        var events = await eventLog.ListRecentAsync(DemoTenantId, limit: 200, cancellationToken);
        var count = events.Count;
        return count == 1 ? "1 access event on record." : $"{count} access events on record.";
    }
}

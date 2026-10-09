// Developer tests for BIO-RPT-004. SYNTHETIC fixtures only.
using Biolog.Features.OperationalDashboard.Domain;
using Xunit;

namespace Biolog.Features.OperationalDashboard.Tests;

public sealed class DashboardServiceTests
{
    private const string TenantA = "ten_018f2e5a-1234-7abc-89ab-0123456789ab";
    private const string TenantB = "ten_018f2e5a-5678-7def-89ab-0123456789ab";
    private const string GrantedActor = "per_018f2e5a-aaaa-7abc-89ab-0123456789ab";
    private const string UngrantedActor = "per_018f2e5a-bbbb-7abc-89ab-0123456789ab";

    private static (DashboardService Service, FakePermissionGateway Permissions, FakeIdentityHealthGateway Identity, FakeAccessHealthGateway Access, FakeProductionHealthGateway Production) Build(string? tenantId)
    {
        var permissions = new FakePermissionGateway();
        permissions.Grant(GrantedActor);
        var identity = new FakeIdentityHealthGateway();
        var access = new FakeAccessHealthGateway();
        var production = new FakeProductionHealthGateway();
        var service = new DashboardService(new FakeCurrentTenantProvider(tenantId), permissions, identity, access, production, new LastKnownHealthCache());
        return (service, permissions, identity, access, production);
    }

    [Fact]
    public async Task AC01_the_snapshot_carries_the_server_resolved_tenant_never_a_caller_supplied_one()
    {
        var (service, _, _, _, _) = Build(TenantA);

        var snapshot = await service.GetSnapshotAsync(GrantedActor, TestContext.Current.CancellationToken);

        Assert.Equal(TenantA, snapshot.TenantId);
    }

    [Fact]
    public async Task AC01_identity_access_and_production_panels_reflect_the_real_tenant_scoped_gateway_counts()
    {
        var (service, _, identity, access, production) = Build(TenantA);
        identity.Seed(TenantA, new IdentityCounts(10, 7, 2, 1));
        access.Seed(TenantA, new AccessCounts(5, 3, 2));
        production.Seed(TenantA, new ProductionCounts(4, 2, 1, 0, 1, 0));

        var snapshot = await service.GetSnapshotAsync(GrantedActor, TestContext.Current.CancellationToken);

        Assert.Equal((10, 7, 2, 1), (snapshot.Identity.Total, snapshot.Identity.Active, snapshot.Identity.Suspended, snapshot.Identity.Other));
        Assert.Equal((5, 3, 2), (snapshot.Access.Total, snapshot.Access.Allowed, snapshot.Access.Denied));
        Assert.Equal((4, 2, 1, 0, 1, 0), (snapshot.Production.Total, snapshot.Production.Passed, snapshot.Production.Failed, snapshot.Production.Rejected, snapshot.Production.InProgress, snapshot.Production.Cancelled));
    }

    /// <summary>Seyi round-1, BIO-RPT-004-F01: tenant scoping alone is not authorization.</summary>
    [Fact]
    public async Task F01_a_caller_without_the_dashboard_permission_is_forbidden_before_any_gateway_is_ever_called()
    {
        var (service, _, identity, access, production) = Build(TenantA);
        identity.Seed(TenantA, new IdentityCounts(999, 999, 0, 0)); // would be a dead giveaway if the forbidden path leaked it

        var error = await Assert.ThrowsAsync<DashboardError>(() => service.GetSnapshotAsync(UngrantedActor, TestContext.Current.CancellationToken));

        Assert.Equal("dashboard_view_forbidden", error.Code);
        Assert.Equal(403, error.Status);
    }

    [Fact]
    public async Task F01_an_ungranted_actor_never_causes_a_single_health_gateway_call()
    {
        var (service, _, identity, _, _) = Build(TenantA);

        await Assert.ThrowsAsync<DashboardError>(() => service.GetSnapshotAsync(UngrantedActor, TestContext.Current.CancellationToken));

        Assert.Equal(0, identity.CallCount);
    }

    [Fact]
    public async Task F01_a_caller_with_the_dashboard_permission_succeeds()
    {
        var (service, _, identity, _, _) = Build(TenantA);
        identity.Seed(TenantA, new IdentityCounts(1, 1, 0, 0));

        var snapshot = await service.GetSnapshotAsync(GrantedActor, TestContext.Current.CancellationToken);

        Assert.Equal(1, snapshot.Identity.Total);
    }

    [Fact]
    public async Task AC03_the_three_real_panels_are_Live_and_the_three_unavailable_panels_are_Unavailable_with_a_stated_reason_and_never_look_the_same()
    {
        var (service, _, _, _, _) = Build(TenantA);

        var snapshot = await service.GetSnapshotAsync(GrantedActor, TestContext.Current.CancellationToken);

        Assert.Equal(DataFreshness.Live, snapshot.Identity.Freshness);
        Assert.Equal(DataFreshness.Live, snapshot.Access.Freshness);
        Assert.Equal(DataFreshness.Live, snapshot.Production.Freshness);

        Assert.Equal(DataFreshness.Unavailable, snapshot.Card.Freshness);
        Assert.Equal(DataFreshness.Unavailable, snapshot.Device.Freshness);
        Assert.Equal(DataFreshness.Unavailable, snapshot.Sync.Freshness);
        Assert.NotEmpty(snapshot.Card.Reason);
        Assert.NotEmpty(snapshot.Device.Reason);
        Assert.NotEmpty(snapshot.Sync.Reason);

        // F02: Unavailable is never the same wire/enum value as Stale - the two states this
        // review finding required be kept visually and semantically distinct.
        Assert.NotEqual(DataFreshness.Stale, snapshot.Card.Freshness);
    }

    [Fact]
    public async Task AC03_every_panel_carries_its_own_ObservedAt_timestamp()
    {
        var (service, _, _, _, _) = Build(TenantA);

        var snapshot = await service.GetSnapshotAsync(GrantedActor, TestContext.Current.CancellationToken);

        Assert.NotEmpty(snapshot.Identity.ObservedAt);
        Assert.NotEmpty(snapshot.Access.ObservedAt);
        Assert.NotEmpty(snapshot.Production.ObservedAt);
        Assert.NotEmpty(snapshot.Card.ObservedAt);
        Assert.NotEmpty(snapshot.Device.ObservedAt);
        Assert.NotEmpty(snapshot.Sync.ObservedAt);
    }

    /// <summary>Seyi round-1, BIO-RPT-004-F02: "derive stale/offline status ... rather than hard-coding Live." A failing real source degrades only its OWN panel, never the whole snapshot.</summary>
    [Fact]
    public async Task F02_a_failing_identity_gateway_degrades_only_the_identity_panel_to_Stale_not_the_whole_snapshot()
    {
        var (service, _, identity, access, production) = Build(TenantA);
        identity.ThrowOnNextCall();
        access.Seed(TenantA, new AccessCounts(5, 3, 2));
        production.Seed(TenantA, new ProductionCounts(4, 2, 1, 0, 1, 0));

        var snapshot = await service.GetSnapshotAsync(GrantedActor, TestContext.Current.CancellationToken);

        Assert.Equal(DataFreshness.Stale, snapshot.Identity.Freshness);
        Assert.NotNull(snapshot.Identity.Reason);

        // The other two real sources are unaffected - this is fault ISOLATION, not a global failure.
        Assert.Equal(DataFreshness.Live, snapshot.Access.Freshness);
        Assert.Equal(DataFreshness.Live, snapshot.Production.Freshness);
        Assert.Equal(5, snapshot.Access.Total);
        Assert.Equal(4, snapshot.Production.Total);
    }

    [Fact]
    public async Task F02_a_failing_access_gateway_degrades_only_the_access_panel()
    {
        var (service, _, _, access, _) = Build(TenantA);
        access.ThrowOnNextCall();

        var snapshot = await service.GetSnapshotAsync(GrantedActor, TestContext.Current.CancellationToken);

        Assert.Equal(DataFreshness.Stale, snapshot.Access.Freshness);
        Assert.NotNull(snapshot.Access.Reason);
        Assert.Equal(DataFreshness.Live, snapshot.Identity.Freshness);
        Assert.Equal(DataFreshness.Live, snapshot.Production.Freshness);
    }

    [Fact]
    public async Task F02_a_failing_production_gateway_degrades_only_the_production_panel()
    {
        var (service, _, _, _, production) = Build(TenantA);
        production.ThrowOnNextCall();

        var snapshot = await service.GetSnapshotAsync(GrantedActor, TestContext.Current.CancellationToken);

        Assert.Equal(DataFreshness.Stale, snapshot.Production.Freshness);
        Assert.NotNull(snapshot.Production.Reason);
        Assert.Equal(DataFreshness.Live, snapshot.Identity.Freshness);
        Assert.Equal(DataFreshness.Live, snapshot.Access.Freshness);
    }

    /// <summary>Seyi round-2, BIO-RPT-004-F04 (High): a Stale panel must never fabricate a zero - when nothing has ever succeeded for this tenant, no counts are reported at all.</summary>
    [Fact]
    public async Task F04_a_source_that_has_never_once_succeeded_reports_no_counts_at_all_never_a_fabricated_zero()
    {
        var (service, _, identity, _, _) = Build(TenantA);
        identity.ThrowOnNextCall();

        var snapshot = await service.GetSnapshotAsync(GrantedActor, TestContext.Current.CancellationToken);

        Assert.Equal(DataFreshness.Stale, snapshot.Identity.Freshness);
        Assert.Null(snapshot.Identity.Total);
        Assert.Null(snapshot.Identity.Active);
        Assert.Null(snapshot.Identity.Suspended);
        Assert.Null(snapshot.Identity.Other);
        Assert.Contains("no successful read has ever been observed", snapshot.Identity.Reason);
    }

    /// <summary>Seyi round-2, BIO-RPT-004-F04: a source that HAS succeeded before retains those real values (and their true observation time) on a later failure, rather than a fresh fabricated zero.</summary>
    [Fact]
    public async Task F04_a_source_that_previously_succeeded_retains_its_last_known_values_and_their_true_observation_time_on_a_later_failure()
    {
        var (service, _, identity, _, _) = Build(TenantA);
        identity.Seed(TenantA, new IdentityCounts(10, 7, 2, 1));

        var firstSnapshot = await service.GetSnapshotAsync(GrantedActor, TestContext.Current.CancellationToken);
        Assert.Equal(DataFreshness.Live, firstSnapshot.Identity.Freshness);
        var lastGoodObservedAt = firstSnapshot.Identity.ObservedAt;

        identity.ThrowOnNextCall();
        var secondSnapshot = await service.GetSnapshotAsync(GrantedActor, TestContext.Current.CancellationToken);

        Assert.Equal(DataFreshness.Stale, secondSnapshot.Identity.Freshness);
        // The RETAINED real values, not zeros:
        Assert.Equal(10, secondSnapshot.Identity.Total);
        Assert.Equal(7, secondSnapshot.Identity.Active);
        Assert.Equal(2, secondSnapshot.Identity.Suspended);
        Assert.Equal(1, secondSnapshot.Identity.Other);
        // The TRUE observation time of that last success, not the failed attempt's own time:
        Assert.Equal(lastGoodObservedAt, secondSnapshot.Identity.ObservedAt);
        Assert.Contains("last successfully observed values", secondSnapshot.Identity.Reason);
    }

    [Fact]
    public async Task AC04_a_request_resolved_to_tenant_A_never_sees_tenant_Bs_counts_even_though_both_are_seeded()
    {
        var (service, _, identity, _, _) = Build(TenantA);
        identity.Seed(TenantA, new IdentityCounts(1, 1, 0, 0));
        identity.Seed(TenantB, new IdentityCounts(999, 999, 0, 0));

        var snapshot = await service.GetSnapshotAsync(GrantedActor, TestContext.Current.CancellationToken);

        Assert.Equal(1, snapshot.Identity.Total);
        Assert.NotEqual(999, snapshot.Identity.Total);
    }

    [Fact]
    public async Task AC04_there_is_no_overload_anywhere_on_DashboardService_that_accepts_a_tenant_id_parameter()
    {
        // Structural guarantee, not merely behavioural: a caller cannot even ASK this service for
        // a tenant other than the one the host channel resolved for their own request (ADR-0002
        // Decision 2's "no default tenant" discipline, applied to "no caller-chosen tenant" too).
        // actorId IS a legitimate parameter (BIO-RPT-004-F01's own authorization gate) - a
        // different concept from tenant, so this guarantee still holds with it present.
        var methods = typeof(DashboardService).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly);
        foreach (var method in methods)
        {
            foreach (var parameter in method.GetParameters())
            {
                Assert.NotEqual("tenantId", parameter.Name, StringComparer.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public async Task no_tenant_resolved_for_the_request_fails_closed_with_tenant_context_missing_rather_than_a_default()
    {
        var (service, _, _, _, _) = Build(tenantId: null);

        var error = await Assert.ThrowsAsync<DashboardError>(() => service.GetSnapshotAsync(GrantedActor, TestContext.Current.CancellationToken));

        Assert.Equal("tenant_context_missing", error.Code);
        Assert.Equal(400, error.Status);
    }
}

using Biolog.Features.Visitors.Api;
using Biolog.Features.Visitors.Audit;
using Biolog.Features.Visitors.Domain;
using Xunit;

namespace Biolog.Features.Visitors.Tests;

/// <summary>AC03 (tenant feature config) - one of this feature's two authorization decisions (ADR-0006 Decision 1/2).</summary>
public class VisitorConfigServiceTests
{
    private const string TenantId = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77";
    private static readonly AuditActor Actor = new(ActorType.User, "usr-admin-1");

    private static VisitorConfigService MakeService(out InMemoryVisitorConfigRepository repository, out InMemoryVisitorAuditSink auditSink, IPermissionGateway? permissionGateway = null)
    {
        repository = new InMemoryVisitorConfigRepository();
        auditSink = new InMemoryVisitorAuditSink();
        return new VisitorConfigService(new FakeCurrentTenantProvider(TenantId), repository, auditSink, new VisitorConfigLockRegistry(), permissionGateway ?? new FakePermissionGateway());
    }

    [Fact]
    public async Task AC03_a_a_tenant_with_no_explicit_config_still_has_an_effective_default_requiring_nothing()
    {
        var service = MakeService(out _, out _);
        var config = await service.GetAsync();

        Assert.False(config.RequireNda);
        Assert.False(config.RequireVehiclePlate);
        Assert.False(config.RequireSafetyBriefing);
        Assert.False(config.RequireWatchlistScreening);
        Assert.False(config.AllowTemporaryCredential);
        Assert.Equal(0, config.Version);
    }

    [Fact]
    public async Task AC03_b_setting_config_requires_the_manage_permission()
    {
        var denyingGateway = new FakePermissionGateway(new HashSet<(string, string)>());
        var service = MakeService(out _, out _, denyingGateway);
        var request = new SetVisitorConfigRequest { RequireNda = true, DefaultVisitDurationHours = 8, MaxVisitDurationHours = 24 };

        var ex = await Assert.ThrowsAsync<VisitorError>(() => service.SetAsync(request, Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f25"));
        Assert.Equal("permission_required", ex.Code);
        Assert.Equal(403, ex.Status);
    }

    [Fact]
    public async Task AC03_c_setting_config_persists_it_advances_the_version_and_audits_it()
    {
        var service = MakeService(out var repository, out var auditSink);
        var request = new SetVisitorConfigRequest { RequireNda = true, RequireVehiclePlate = true, DefaultVisitDurationHours = 8, MaxVisitDurationHours = 24 };

        var updated = await service.SetAsync(request, Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f25");

        Assert.Equal(1, updated.Version);
        Assert.True(updated.RequireNda);
        Assert.True(updated.RequireVehiclePlate);
        var reread = await repository.GetAsync(TenantId);
        Assert.Equal(1, reread.Version);
        Assert.Contains(auditSink.All(), e => e.Action == "visitor.config_updated");
    }

    [Fact]
    public async Task AC03_d_max_visit_duration_below_default_is_rejected()
    {
        var service = MakeService(out _, out _);
        var request = new SetVisitorConfigRequest { DefaultVisitDurationHours = 24, MaxVisitDurationHours = 8 };

        await Assert.ThrowsAsync<VisitorError>(() => service.SetAsync(request, Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f25"));
    }
}

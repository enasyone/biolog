using Biolog.Features.TransportEvents.Api;
using Biolog.Features.TransportEvents.Audit;
using Biolog.Features.TransportEvents.Domain;
using Xunit;

namespace Biolog.Features.TransportEvents.Tests;

/// <summary>AC04's tenant notification-trigger config - CAS-versioned, never gated by a permission check (see <see cref="TransportNotificationConfig"/>'s own header for the re-derivation).</summary>
public class TransportNotificationConfigServiceTests
{
    private const string TenantId = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77";
    private static readonly AuditActor Actor = new(ActorType.User, "usr-admin-1");

    private static (TransportNotificationConfigService Service, InMemoryTransportNotificationConfigRepository Repository) MakeHarness()
    {
        var repository = new InMemoryTransportNotificationConfigRepository();
        var service = new TransportNotificationConfigService(
            new FakeCurrentTenantProvider(TenantId), repository, new InMemoryTransportEventAuditSink(), new TransportNotificationConfigLockRegistry());
        return (service, repository);
    }

    [Fact]
    public async Task AC04_a_a_tenant_with_no_explicit_config_still_has_a_real_effective_default_with_notifications_off()
    {
        var (service, _) = MakeHarness();

        var config = await service.GetAsync();

        Assert.False(config.NotifyOnMissingAlightException);
        Assert.Equal(0, config.Version);
    }

    [Fact]
    public async Task AC04_b_setting_the_config_the_first_time_creates_version_1_no_permission_check_required()
    {
        var (service, _) = MakeHarness();

        var updated = await service.SetAsync(new SetTransportNotificationConfigRequest { NotifyOnMissingAlightException = true }, Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f20");

        Assert.True(updated.NotifyOnMissingAlightException);
        Assert.Equal(1, updated.Version);
    }

    [Fact]
    public async Task AC04_c_setting_again_increments_the_version_and_the_new_value_is_read_back()
    {
        var (service, _) = MakeHarness();
        await service.SetAsync(new SetTransportNotificationConfigRequest { NotifyOnMissingAlightException = true }, Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f20");

        var updated = await service.SetAsync(new SetTransportNotificationConfigRequest { NotifyOnMissingAlightException = false }, Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f21");

        Assert.False(updated.NotifyOnMissingAlightException);
        Assert.Equal(2, updated.Version);
        var reread = await service.GetAsync();
        Assert.Equal(2, reread.Version);
        Assert.False(reread.NotifyOnMissingAlightException);
    }

    [Fact]
    public async Task AC04_d_a_stale_concurrent_write_is_a_concurrency_conflict_not_silently_overwritten()
    {
        var repository = new InMemoryTransportNotificationConfigRepository();
        var serviceA = new TransportNotificationConfigService(new FakeCurrentTenantProvider(TenantId), repository, new InMemoryTransportEventAuditSink(), new TransportNotificationConfigLockRegistry());
        var serviceB = new TransportNotificationConfigService(new FakeCurrentTenantProvider(TenantId), repository, new InMemoryTransportEventAuditSink(), new TransportNotificationConfigLockRegistry());

        // Both services share the SAME underlying repository (like two concurrent requests would),
        // but each has its own lock registry instance - simulating a stale write that slips past a
        // single process's own lock (e.g. two replicas), proving the CAS check itself is the real
        // guarantee, not merely the in-process lock.
        await serviceA.SetAsync(new SetTransportNotificationConfigRequest { NotifyOnMissingAlightException = true }, Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f22");

        var directSave = await repository.TrySaveAsync(
            TransportNotificationConfig.Default(TenantId), expectedVersion: 0, CancellationToken.None);

        Assert.False(directSave); // version 0 is now stale - real version is 1
    }
}

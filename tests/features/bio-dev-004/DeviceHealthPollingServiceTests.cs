using Xunit;

namespace Biolog.Features.DeviceOperations.Tests;

/// <summary>BIO-DEV-004-AC01 ("last-seen/status/health are updated") and AC04 ("unsupported
/// remote actions fail explicitly") for the health-polling side. SY-DEV004-01: every call now
/// also requires <see cref="DeviceHealthPollingService.ReadPermission"/> - a granted-by-default
/// <see cref="FakePermissionGateway"/> keeps the pre-existing feature-logic tests focused on
/// what they already proved, with one dedicated test below proving the gate itself.</summary>
public sealed class DeviceHealthPollingServiceTests
{
    private const string TestTenantId = "ten_a";
    private static readonly AuditActor TestActor = new("user", "usr_ops");

    private static DeviceHealthPollingService Build(
        FakeDeviceDirectoryGateway directory, IDeviceHealthCheckGateway healthCheck, FakeDeviceHeartbeatWriter writer,
        FakePermissionGateway? permissionGateway = null) =>
        new(directory, healthCheck, writer, permissionGateway ?? new FakePermissionGateway());

    [Fact]
    public async Task A_successful_poll_writes_the_checked_health_and_returns_the_updated_device()
    {
        var directory = new FakeDeviceDirectoryGateway();
        directory.Add(SampleDevices.Certified("dev_1"));
        var checkedAt = DateTimeOffset.Parse("2026-10-07T10:00:00Z");
        var healthCheck = FakeDeviceHealthCheckGateway.Returning(new DeviceHealthSnapshot(DeviceHealthStatus.Online, "ping ok", checkedAt));
        var writer = new FakeDeviceHeartbeatWriter();
        var service = Build(directory, healthCheck, writer);

        var result = await service.PollHealthAsync(TestTenantId, "dev_1", TestActor, correlationId: null, CancellationToken.None);

        Assert.Equal(1, writer.CallCount);
        Assert.Equal(("dev_1", DeviceHealthStatus.Online, "ping ok", checkedAt, 1), writer.LastCall);
        Assert.Equal(DeviceHealthStatus.Online, result.Device.Health);
        Assert.Equal(checkedAt, result.Device.LastSeenAt);
        Assert.Equal(2, result.Device.Version);
    }

    [Fact]
    public async Task Polling_an_unknown_device_fails_explicitly_as_not_found()
    {
        var directory = new FakeDeviceDirectoryGateway();
        var healthCheck = FakeDeviceHealthCheckGateway.Returning(new DeviceHealthSnapshot(DeviceHealthStatus.Online, null, DateTimeOffset.UtcNow));
        var writer = new FakeDeviceHeartbeatWriter();
        var service = Build(directory, healthCheck, writer);

        var ex = await Assert.ThrowsAsync<DeviceNotFoundException>(
            () => service.PollHealthAsync(TestTenantId, "dev_missing", TestActor, null, CancellationToken.None));
        Assert.Equal("dev_missing", ex.DeviceId);
        Assert.Equal(0, writer.CallCount);
    }

    [Fact]
    public async Task Polling_an_uncertified_combination_fails_explicitly_rather_than_reporting_a_fake_offline_reading()
    {
        var directory = new FakeDeviceDirectoryGateway();
        directory.Add(SampleDevices.Certified("dev_1"));
        var service = Build(directory, FakeDeviceHealthCheckGateway.ThrowingUnsupported(), new FakeDeviceHeartbeatWriter());

        await Assert.ThrowsAsync<DeviceCombinationUnsupportedException>(
            () => service.PollHealthAsync(TestTenantId, "dev_1", TestActor, null, CancellationToken.None));
    }

    [Fact]
    public async Task Polling_a_certified_device_with_no_registered_adapter_fails_explicitly()
    {
        var directory = new FakeDeviceDirectoryGateway();
        directory.Add(SampleDevices.Certified("dev_1"));
        var service = Build(directory, FakeDeviceHealthCheckGateway.ThrowingAdapterUnavailable(), new FakeDeviceHeartbeatWriter());

        await Assert.ThrowsAsync<DeviceAdapterUnavailableException>(
            () => service.PollHealthAsync(TestTenantId, "dev_1", TestActor, null, CancellationToken.None));
    }

    [Fact]
    public async Task A_concurrency_conflict_from_the_writer_is_never_swallowed()
    {
        var directory = new FakeDeviceDirectoryGateway();
        directory.Add(SampleDevices.Certified("dev_1"));
        var writer = new FakeDeviceHeartbeatWriter { ThrowConflict = true };
        var service = Build(
            directory, FakeDeviceHealthCheckGateway.Returning(new DeviceHealthSnapshot(DeviceHealthStatus.Online, null, DateTimeOffset.UtcNow)), writer);

        await Assert.ThrowsAsync<HeartbeatConcurrencyConflictException>(
            () => service.PollHealthAsync(TestTenantId, "dev_1", TestActor, null, CancellationToken.None));
    }

    [Fact]
    public async Task Listing_devices_returns_everything_the_directory_gateway_reports()
    {
        var directory = new FakeDeviceDirectoryGateway();
        directory.Add(SampleDevices.Certified("dev_1"));
        directory.Add(SampleDevices.Certified("dev_2"));
        var service = Build(directory, FakeDeviceHealthCheckGateway.Returning(new DeviceHealthSnapshot(DeviceHealthStatus.Online, null, DateTimeOffset.UtcNow)), new FakeDeviceHeartbeatWriter());

        var devices = await service.ListDevicesAsync(TestTenantId, TestActor, CancellationToken.None);

        Assert.Equal(2, devices.Count);
    }

    /// <summary>SY-DEV004-01: an actor who does not hold <see cref="DeviceHealthPollingService.ReadPermission"/>
    /// is refused BEFORE either operation reaches the directory/health-check/writer gateways at
    /// all - not a 200 with a filtered result, a real, distinct refusal.</summary>
    [Fact]
    public async Task An_actor_without_the_read_permission_is_refused_before_any_device_is_touched()
    {
        var directory = new FakeDeviceDirectoryGateway();
        directory.Add(SampleDevices.Certified("dev_1"));
        var writer = new FakeDeviceHeartbeatWriter();
        var permissionGateway = new FakePermissionGateway();
        permissionGateway.Deny(DeviceHealthPollingService.ReadPermission);
        var service = Build(
            directory, FakeDeviceHealthCheckGateway.Returning(new DeviceHealthSnapshot(DeviceHealthStatus.Online, null, DateTimeOffset.UtcNow)),
            writer, permissionGateway);

        var listEx = await Assert.ThrowsAsync<PermissionRequiredException>(
            () => service.ListDevicesAsync(TestTenantId, TestActor, CancellationToken.None));
        Assert.Equal(DeviceHealthPollingService.ReadPermission, listEx.PermissionKey);

        var pollEx = await Assert.ThrowsAsync<PermissionRequiredException>(
            () => service.PollHealthAsync(TestTenantId, "dev_1", TestActor, null, CancellationToken.None));
        Assert.Equal(DeviceHealthPollingService.ReadPermission, pollEx.PermissionKey);
        Assert.Equal(0, writer.CallCount);
    }
}

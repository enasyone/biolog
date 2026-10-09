using Xunit;

namespace Biolog.Features.DeviceOperations.Tests;

/// <summary>BIO-DEV-004-AC02 ("remote jobs are capability-gated"), AC03 ("firmware/config
/// changes require audit") and AC04 ("unsupported remote actions fail explicitly"). SY-DEV004-01:
/// every call now also requires either <see cref="DeviceHealthPollingService.ReadPermission"/>
/// (capabilities read) or <see cref="DeviceRemoteJobService.ExecutePermission"/> (running a
/// job) - a granted-by-default <see cref="FakePermissionGateway"/> keeps the pre-existing
/// feature-logic tests focused on what they already proved, with dedicated tests below proving
/// the gate itself.</summary>
public sealed class DeviceRemoteJobServiceTests
{
    private const string TestTenantId = "ten_a";
    private static readonly AuditActor TestActor = new("user", "usr_ops");

    private static (DeviceRemoteJobService Service, FakeDeviceDirectoryGateway Directory, FakeRemoteJobAuditGateway Audit, OrderAssertingRemoteJobExecutor Executor, FakePermissionGateway PermissionGateway) Build(
        IRemoteJobCapabilityGateway capabilityGateway, FakePermissionGateway? permissionGateway = null)
    {
        var directory = new FakeDeviceDirectoryGateway();
        var audit = new FakeRemoteJobAuditGateway();
        var executor = new OrderAssertingRemoteJobExecutor(audit);
        var permissions = permissionGateway ?? new FakePermissionGateway();
        var service = new DeviceRemoteJobService(directory, capabilityGateway, audit, executor, permissions);
        return (service, directory, audit, executor, permissions);
    }

    [Fact]
    public async Task A_supported_job_is_audited_before_it_is_executed_and_the_outcome_is_returned()
    {
        var (service, directory, audit, executor, _) = Build(FakeRemoteJobCapabilityGateway.Supporting(RemoteJobType.Reboot));
        directory.Add(SampleDevices.Certified("dev_1"));

        var result = await service.RunJobAsync(TestTenantId, TestActor, "dev_1", RemoteJobType.Reboot, correlationId: "evt_corr", CancellationToken.None);

        Assert.Equal(RemoteJobOutcome.Succeeded, result.Outcome);
        Assert.Equal(1, executor.CallCount);
        Assert.Single(audit.Records);
        Assert.True(executor.AuditHadAtLeastOneRecordWhenExecuteRan, "AC03: the audit record must already exist before execution is attempted.");
        var recorded = audit.Records[0];
        Assert.Equal("dev_1", recorded.DeviceId);
        Assert.Equal(RemoteJobType.Reboot, recorded.JobType);
        Assert.Equal("ten_a", recorded.TenantId);
        Assert.Equal("evt_corr", recorded.CorrelationId);
    }

    [Fact]
    public async Task A_job_type_the_devices_category_does_not_support_is_rejected_before_any_audit_or_execution()
    {
        var (service, directory, audit, executor, _) = Build(FakeRemoteJobCapabilityGateway.Supporting(RemoteJobType.Reboot));
        directory.Add(SampleDevices.Certified("dev_1"));

        var ex = await Assert.ThrowsAsync<RemoteJobNotSupportedException>(
            () => service.RunJobAsync(TestTenantId, TestActor, "dev_1", RemoteJobType.UpdateFirmware, null, CancellationToken.None));

        Assert.Equal(RemoteJobType.UpdateFirmware, ex.JobType);
        Assert.Empty(audit.Records);
        Assert.Equal(0, executor.CallCount);
    }

    [Fact]
    public async Task A_job_against_an_uncertified_combination_fails_explicitly_before_any_audit_or_execution()
    {
        var (service, directory, audit, executor, _) = Build(FakeRemoteJobCapabilityGateway.ThrowingUnsupported());
        directory.Add(SampleDevices.Certified("dev_1"));

        await Assert.ThrowsAsync<DeviceCombinationUnsupportedException>(
            () => service.RunJobAsync(TestTenantId, TestActor, "dev_1", RemoteJobType.Reboot, null, CancellationToken.None));

        Assert.Empty(audit.Records);
        Assert.Equal(0, executor.CallCount);
    }

    [Fact]
    public async Task A_job_against_a_certified_device_with_no_registered_adapter_fails_explicitly()
    {
        var (service, directory, audit, executor, _) = Build(FakeRemoteJobCapabilityGateway.ThrowingAdapterUnavailable());
        directory.Add(SampleDevices.Certified("dev_1"));

        await Assert.ThrowsAsync<DeviceAdapterUnavailableException>(
            () => service.RunJobAsync(TestTenantId, TestActor, "dev_1", RemoteJobType.Reboot, null, CancellationToken.None));

        Assert.Empty(audit.Records);
        Assert.Equal(0, executor.CallCount);
    }

    [Fact]
    public async Task A_job_against_an_unknown_device_fails_explicitly_as_not_found()
    {
        var (service, _, audit, executor, _) = Build(FakeRemoteJobCapabilityGateway.Supporting(RemoteJobType.Reboot));

        await Assert.ThrowsAsync<DeviceNotFoundException>(
            () => service.RunJobAsync(TestTenantId, TestActor, "dev_missing", RemoteJobType.Reboot, null, CancellationToken.None));

        Assert.Empty(audit.Records);
        Assert.Equal(0, executor.CallCount);
    }

    [Fact]
    public async Task Supported_job_types_are_reported_for_a_known_device_and_not_found_for_an_unknown_one()
    {
        var (service, directory, _, _, _) = Build(FakeRemoteJobCapabilityGateway.Supporting(RemoteJobType.Reboot, RemoteJobType.PushConfiguration));
        directory.Add(SampleDevices.Certified("dev_1"));

        var supported = await service.GetSupportedJobTypesAsync(TestTenantId, TestActor, "dev_1", CancellationToken.None);
        Assert.Equal([RemoteJobType.Reboot, RemoteJobType.PushConfiguration], supported);

        await Assert.ThrowsAsync<DeviceNotFoundException>(() => service.GetSupportedJobTypesAsync(TestTenantId, TestActor, "dev_missing", CancellationToken.None));
    }

    [Fact]
    public async Task A_failed_execution_outcome_is_still_returned_to_the_caller_not_swallowed()
    {
        var (service, directory, audit, executor, _) = Build(FakeRemoteJobCapabilityGateway.Supporting(RemoteJobType.Reboot));
        directory.Add(SampleDevices.Certified("dev_1"));
        executor.OutcomeToReturn = RemoteJobOutcome.Failed;
        executor.DetailToReturn = "vendor link down (test)";

        var result = await service.RunJobAsync(TestTenantId, TestActor, "dev_1", RemoteJobType.Reboot, null, CancellationToken.None);

        Assert.Equal(RemoteJobOutcome.Failed, result.Outcome);
        Assert.Equal("vendor link down (test)", result.Detail);
        // AC03: the job was still audited even though it ultimately failed - the audit
        // records that the actor ran it, not that it necessarily succeeded.
        Assert.Single(audit.Records);
    }

    /// <summary>SY-DEV004-01: capabilities-read is gated by the lower-privilege
    /// <see cref="DeviceHealthPollingService.ReadPermission"/>; actually running a job is
    /// gated by the strictly higher-privilege <see cref="DeviceRemoteJobService.ExecutePermission"/>
    /// - an actor who only holds the read permission can see what a device supports but cannot
    /// run anything against it.</summary>
    [Fact]
    public async Task An_actor_without_the_execute_permission_is_refused_before_any_audit_or_execution_even_if_it_can_read_capabilities()
    {
        var permissionGateway = new FakePermissionGateway();
        permissionGateway.Deny(DeviceRemoteJobService.ExecutePermission);
        var (service, directory, audit, executor, _) = Build(FakeRemoteJobCapabilityGateway.Supporting(RemoteJobType.Reboot), permissionGateway);
        directory.Add(SampleDevices.Certified("dev_1"));

        var supported = await service.GetSupportedJobTypesAsync(TestTenantId, TestActor, "dev_1", CancellationToken.None);
        Assert.Equal([RemoteJobType.Reboot], supported);

        var ex = await Assert.ThrowsAsync<PermissionRequiredException>(
            () => service.RunJobAsync(TestTenantId, TestActor, "dev_1", RemoteJobType.Reboot, null, CancellationToken.None));
        Assert.Equal(DeviceRemoteJobService.ExecutePermission, ex.PermissionKey);
        Assert.Empty(audit.Records);
        Assert.Equal(0, executor.CallCount);
    }

    [Fact]
    public async Task An_actor_without_the_read_permission_cannot_even_read_capabilities()
    {
        var permissionGateway = new FakePermissionGateway();
        permissionGateway.Deny(DeviceHealthPollingService.ReadPermission);
        var (service, directory, _, _, _) = Build(FakeRemoteJobCapabilityGateway.Supporting(RemoteJobType.Reboot), permissionGateway);
        directory.Add(SampleDevices.Certified("dev_1"));

        var ex = await Assert.ThrowsAsync<PermissionRequiredException>(
            () => service.GetSupportedJobTypesAsync(TestTenantId, TestActor, "dev_1", CancellationToken.None));
        Assert.Equal(DeviceHealthPollingService.ReadPermission, ex.PermissionKey);
    }
}

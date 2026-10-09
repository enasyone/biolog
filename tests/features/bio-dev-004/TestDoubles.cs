namespace Biolog.Features.DeviceOperations.Tests;

internal sealed class FakeDeviceDirectoryGateway : IDeviceDirectoryGateway
{
    private readonly Dictionary<string, DeviceSnapshot> _devices = new();

    public void Add(DeviceSnapshot device) => _devices[device.Id] = device;

    public Task<DeviceSnapshot?> GetAsync(string deviceId, CancellationToken cancellationToken) =>
        Task.FromResult(_devices.GetValueOrDefault(deviceId));

    public Task<IReadOnlyList<DeviceSnapshot>> ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<DeviceSnapshot>>(_devices.Values.ToArray());
}

internal sealed class FakeDeviceHealthCheckGateway(Func<DeviceSnapshot, DeviceHealthSnapshot> result) : IDeviceHealthCheckGateway
{
    public int CallCount { get; private set; }

    public static FakeDeviceHealthCheckGateway Returning(DeviceHealthSnapshot snapshot) => new(_ => snapshot);

    public static FakeDeviceHealthCheckGateway ThrowingUnsupported() => new(device =>
        throw new DeviceCombinationUnsupportedException(device.Vendor, device.Model, device.Firmware, device.Sdk, "Unsupported", null));

    public static FakeDeviceHealthCheckGateway ThrowingAdapterUnavailable() => new(device =>
        throw new DeviceAdapterUnavailableException(device.Vendor, device.Model));

    public Task<DeviceHealthSnapshot> CheckHealthAsync(DeviceSnapshot device, CancellationToken cancellationToken)
    {
        CallCount++;
        return Task.FromResult(result(device));
    }
}

internal sealed class FakeDeviceHeartbeatWriter : IDeviceHeartbeatWriter
{
    public int CallCount { get; private set; }
    public (string DeviceId, DeviceHealthStatus Health, string? Detail, DateTimeOffset CheckedAt, int ExpectedVersion)? LastCall { get; private set; }
    public bool ThrowConflict { get; set; }
    public bool ReturnNull { get; set; }

    public Task<DeviceSnapshot?> RecordHeartbeatAsync(
        string deviceId, DeviceHealthStatus health, string? detail, DateTimeOffset checkedAt, int expectedVersion,
        AuditActor actor, string? correlationId, CancellationToken cancellationToken)
    {
        CallCount++;
        LastCall = (deviceId, health, detail, checkedAt, expectedVersion);

        if (ThrowConflict)
        {
            throw new HeartbeatConcurrencyConflictException(expectedVersion + 1);
        }

        if (ReturnNull)
        {
            return Task.FromResult<DeviceSnapshot?>(null);
        }

        return Task.FromResult<DeviceSnapshot?>(new DeviceSnapshot(
            deviceId, "ten_a", "Acme", "FS-100", "1.0.0", "sdk-1", "SN-1", "Active",
            checkedAt, health, detail, checkedAt, expectedVersion + 1));
    }
}

internal sealed class FakeRemoteJobCapabilityGateway(Func<DeviceSnapshot, IReadOnlyList<RemoteJobType>> result) : IRemoteJobCapabilityGateway
{
    public static FakeRemoteJobCapabilityGateway Supporting(params RemoteJobType[] jobTypes) => new(_ => jobTypes);

    public static FakeRemoteJobCapabilityGateway ThrowingUnsupported() => new(device =>
        throw new DeviceCombinationUnsupportedException(device.Vendor, device.Model, device.Firmware, device.Sdk, "Unsupported", null));

    public static FakeRemoteJobCapabilityGateway ThrowingAdapterUnavailable() => new(device =>
        throw new DeviceAdapterUnavailableException(device.Vendor, device.Model));

    public Task<IReadOnlyList<RemoteJobType>> GetSupportedJobTypesAsync(DeviceSnapshot device, CancellationToken cancellationToken) =>
        Task.FromResult(result(device));
}

internal sealed class FakeRemoteJobAuditGateway : IRemoteJobAuditGateway
{
    public List<RemoteJobAuditRecord> Records { get; } = [];

    public Task RecordAsync(RemoteJobAuditRecord record, CancellationToken cancellationToken)
    {
        Records.Add(record);
        return Task.CompletedTask;
    }
}

/// <summary>Proves BIO-DEV-004-AC03's audit-BEFORE-execution ordering concretely, not just
/// "both happened": fails the test immediately if the executor runs before any audit record
/// exists, the same "force a real ordering assertion, don't just count calls" discipline
/// this session's barrier-gated concurrency tests already established for a different
/// hazard (reused here for ordering, not concurrency).</summary>
internal sealed class OrderAssertingRemoteJobExecutor(FakeRemoteJobAuditGateway auditGateway) : IRemoteJobExecutor
{
    public int CallCount { get; private set; }
    public bool AuditHadAtLeastOneRecordWhenExecuteRan { get; private set; }
    public RemoteJobOutcome OutcomeToReturn { get; set; } = RemoteJobOutcome.Succeeded;
    public string? DetailToReturn { get; set; } = "ok (test)";

    public Task<(RemoteJobOutcome Outcome, string? Detail)> ExecuteAsync(
        DeviceSnapshot device, RemoteJobType jobType, CancellationToken cancellationToken)
    {
        CallCount++;
        AuditHadAtLeastOneRecordWhenExecuteRan = auditGateway.Records.Count > 0;
        return Task.FromResult((OutcomeToReturn, DetailToReturn));
    }
}

internal static class SampleDevices
{
    public static DeviceSnapshot Certified(string id = "dev_sample1") => new(
        id, "ten_a", "Acme", "FS-100", "1.0.0", "sdk-1", "SN-1", "Active",
        null, null, null, null, 1);
}

/// <summary>SY-DEV004-01: granted-by-default (every test proving the ACTUAL feature logic
/// should not also have to think about permissions), with an explicit opt-out per permission
/// key for the tests that specifically prove the gate itself.</summary>
internal sealed class FakePermissionGateway : IPermissionGateway
{
    private readonly HashSet<string> _denied = [];

    public List<(string TenantId, string ActorId, string PermissionKey)> Calls { get; } = [];

    public void Deny(string permissionKey) => _denied.Add(permissionKey);

    public Task<bool> ActorHasPermissionAsync(string tenantId, string actorId, string permissionKey, CancellationToken cancellationToken = default)
    {
        Calls.Add((tenantId, actorId, permissionKey));
        return Task.FromResult(!_denied.Contains(permissionKey));
    }
}

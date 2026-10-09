namespace Biolog.Features.DeviceOperations;

/// <summary>
/// BIO-DEV-004-AC01: "health polling/heartbeat." A poll is triggered on demand (per device,
/// or swept across every device the resolved tenant can see) rather than run by a real
/// background worker - the same "triggered, not a true scheduled job" shape every other
/// synthetic/demo operational action in this codebase uses (e.g. BIO-TRN-003's own
/// dispatch-on-demand panel action), disclosed explicitly in
/// docs/features/bio-dev-004/README.md's own "What 'polling' means here" section.
///
/// SY-DEV004-01 (Seyi, round 1): both operations now require the resolved actor to hold
/// <see cref="ReadPermission"/> in the calling tenant - "device read/health polling" is the
/// lower-privilege half of the split Seyi's finding required, distinct from
/// <see cref="DeviceRemoteJobService.ExecutePermission"/> for actually running a remote job.
/// </summary>
internal sealed class DeviceHealthPollingService(
    IDeviceDirectoryGateway directory,
    IDeviceHealthCheckGateway healthCheck,
    IDeviceHeartbeatWriter heartbeatWriter,
    IPermissionGateway permissionGateway)
{
    public const string ReadPermission = "device_operations.read";

    public async Task<DeviceHeartbeatResult> PollHealthAsync(
        string tenantId, string deviceId, AuditActor actor, string? correlationId, CancellationToken cancellationToken)
    {
        if (!await permissionGateway.ActorHasPermissionAsync(tenantId, actor.ActorId, ReadPermission, cancellationToken))
        {
            throw new PermissionRequiredException(ReadPermission);
        }

        var device = await directory.GetAsync(deviceId, cancellationToken) ?? throw new DeviceNotFoundException(deviceId);

        // AC04: propagates DeviceCombinationUnsupportedException/DeviceAdapterUnavailableException
        // unchanged - a poll against a combination that could not even be attempted must fail
        // explicitly, not be swallowed into a generic "offline" reading.
        var snapshot = await healthCheck.CheckHealthAsync(device, cancellationToken);

        var updated = await heartbeatWriter.RecordHeartbeatAsync(
            deviceId, snapshot.Status, snapshot.Detail, snapshot.CheckedAt, device.Version, actor, correlationId, cancellationToken)
            // A device visible a moment ago but gone now (deleted, or moved tenant - neither
            // possible in this codebase today, but the writer's own not-found contract still
            // applies) is the same not-found condition as never having been visible at all.
            ?? throw new DeviceNotFoundException(deviceId);

        return new DeviceHeartbeatResult(updated);
    }

    public async Task<IReadOnlyList<DeviceSnapshot>> ListDevicesAsync(
        string tenantId, AuditActor actor, CancellationToken cancellationToken)
    {
        if (!await permissionGateway.ActorHasPermissionAsync(tenantId, actor.ActorId, ReadPermission, cancellationToken))
        {
            throw new PermissionRequiredException(ReadPermission);
        }

        return await directory.ListAsync(cancellationToken);
    }
}

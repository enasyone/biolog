namespace Biolog.Features.DeviceOperations;

/// <summary>
/// BIO-DEV-004-AC02/AC03/AC04: "controlled configuration jobs where vendor APIs permit."
/// Every job is capability-gated (AC02) BEFORE anything is audited or executed, audited
/// (AC03) BEFORE execution is attempted - the same audit-before-commit discipline
/// <c>Features.DeviceRegistry.DeviceService.ChangeStatusAsync</c> established: nothing about
/// a job is ever visible to anyone until its own audit event already durably exists, so a
/// crash between "decided to run it" and "ran it" never produces an executed-but-unaudited
/// job. Every rejection reason is its own distinct, explicit exception (AC04) - never a
/// generic failure.
///
/// SY-DEV004-01 (Seyi, round 1): reading supported job types requires
/// <see cref="DeviceHealthPollingService.ReadPermission"/> (the same lower-privilege read
/// permission - knowing what a device CAN do is a read, not a control action); actually
/// running a job requires the strictly higher-privilege <see cref="ExecutePermission"/> -
/// Seyi's required "separate least-privilege tenant permissions for device read/health
/// polling and remote-job execution."
/// </summary>
internal sealed class DeviceRemoteJobService(
    IDeviceDirectoryGateway directory,
    IRemoteJobCapabilityGateway capabilityGateway,
    IRemoteJobAuditGateway auditGateway,
    IRemoteJobExecutor executor,
    IPermissionGateway permissionGateway)
{
    public const string ExecutePermission = "device_operations.execute";

    public async Task<IReadOnlyList<RemoteJobType>> GetSupportedJobTypesAsync(
        string tenantId, AuditActor actor, string deviceId, CancellationToken cancellationToken)
    {
        if (!await permissionGateway.ActorHasPermissionAsync(tenantId, actor.ActorId, DeviceHealthPollingService.ReadPermission, cancellationToken))
        {
            throw new PermissionRequiredException(DeviceHealthPollingService.ReadPermission);
        }

        var device = await directory.GetAsync(deviceId, cancellationToken) ?? throw new DeviceNotFoundException(deviceId);
        return await capabilityGateway.GetSupportedJobTypesAsync(device, cancellationToken);
    }

    public async Task<RemoteJobResult> RunJobAsync(
        string tenantId, AuditActor actor, string deviceId, RemoteJobType jobType, string? correlationId, CancellationToken cancellationToken)
    {
        if (!await permissionGateway.ActorHasPermissionAsync(tenantId, actor.ActorId, ExecutePermission, cancellationToken))
        {
            throw new PermissionRequiredException(ExecutePermission);
        }

        var device = await directory.GetAsync(deviceId, cancellationToken) ?? throw new DeviceNotFoundException(deviceId);

        // AC04: propagates DeviceCombinationUnsupportedException/DeviceAdapterUnavailableException
        // unchanged when the device itself could not even be reached for a capability check.
        var supported = await capabilityGateway.GetSupportedJobTypesAsync(device, cancellationToken);
        if (!supported.Contains(jobType))
        {
            // AC02/AC04: certified, adapter-available device that simply does not support
            // THIS job type - a distinct, explicit refusal from "could not reach the device
            // at all".
            throw new RemoteJobNotSupportedException(deviceId, jobType);
        }

        var now = DateTimeOffset.UtcNow;

        // AC03: the audit write happens BEFORE execution is ever attempted - once this
        // returns, the job is durably on record regardless of what the (possibly fallible,
        // possibly slow) execution step below does next.
        await auditGateway.RecordAsync(
            new RemoteJobAuditRecord(deviceId, device.TenantId, jobType, actor.ActorType, actor.ActorId, correlationId, now),
            cancellationToken);

        // CancellationToken.None, deliberate: the audit event already exists, so a mere
        // cancellation request arriving after this point must not abandon the job it
        // describes - the same point-of-no-return discipline DeviceService.ChangeStatusAsync
        // applies to its own repository write.
        var (outcome, detail) = await executor.ExecuteAsync(device, jobType, CancellationToken.None);
        return new RemoteJobResult(deviceId, jobType, outcome, detail, now);
    }
}

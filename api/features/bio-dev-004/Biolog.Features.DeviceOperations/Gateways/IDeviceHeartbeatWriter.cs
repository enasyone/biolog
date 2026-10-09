namespace Biolog.Features.DeviceOperations;

/// <summary>BIO-DEV-004-AC01: the seam onto BIO-DEV-001's own new
/// <c>DeviceService.RecordHeartbeatAsync</c>. Throws
/// <see cref="HeartbeatConcurrencyConflictException"/> on a stale <paramref
/// name="expectedVersion"/>; returns <c>null</c> for not-found-or-cross-tenant, same as
/// every other device write.</summary>
public interface IDeviceHeartbeatWriter
{
    Task<DeviceSnapshot?> RecordHeartbeatAsync(
        string deviceId,
        DeviceHealthStatus health,
        string? detail,
        DateTimeOffset checkedAt,
        int expectedVersion,
        AuditActor actor,
        string? correlationId,
        CancellationToken cancellationToken);
}

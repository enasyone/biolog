namespace Biolog.Features.DeviceOperations;

/// <summary>BIO-DEV-004-AC01/AC04: the seam onto BIO-DEV-003's own
/// <c>DeviceAdapterService.CheckHealthAsync</c> (which itself gates on BIO-DEV-002's
/// certification matrix before ever reaching a vendor adapter). Throws
/// <see cref="DeviceCombinationUnsupportedException"/>/<see cref="DeviceAdapterUnavailableException"/>
/// for the two "could not even attempt a health check" cases - never a generic failure
/// (AC04).</summary>
public interface IDeviceHealthCheckGateway
{
    Task<DeviceHealthSnapshot> CheckHealthAsync(DeviceSnapshot device, CancellationToken cancellationToken);
}

/// <summary>The translated result of a single health check - BIO-DEV-003's own
/// <c>DeviceHealth</c>, copied field-for-field into this project's own vocabulary.</summary>
public sealed record DeviceHealthSnapshot(DeviceHealthStatus Status, string? Detail, DateTimeOffset CheckedAt);

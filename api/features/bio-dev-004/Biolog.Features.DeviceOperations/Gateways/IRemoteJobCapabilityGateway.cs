namespace Biolog.Features.DeviceOperations;

/// <summary>BIO-DEV-004-AC02/AC04: which <see cref="RemoteJobType"/>s a device's resolved
/// category supports ("where vendor APIs permit") - gated the SAME way BIO-DEV-003's own
/// <c>DeviceAdapterService</c> gates capability discovery: certification first
/// (<see cref="DeviceCombinationUnsupportedException"/>), then adapter resolution
/// (<see cref="DeviceAdapterUnavailableException"/>), and only then the job-type support
/// table itself.</summary>
public interface IRemoteJobCapabilityGateway
{
    Task<IReadOnlyList<RemoteJobType>> GetSupportedJobTypesAsync(DeviceSnapshot device, CancellationToken cancellationToken);
}

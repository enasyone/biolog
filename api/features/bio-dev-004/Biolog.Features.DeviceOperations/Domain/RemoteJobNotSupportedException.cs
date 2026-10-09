namespace Biolog.Features.DeviceOperations;

/// <summary>BIO-DEV-004-AC02/AC04: the device's resolved category does not declare support
/// for this job type ("where vendor APIs permit") - a certified, adapter-available device
/// can still explicitly refuse a specific job type, distinct from
/// <see cref="DeviceCombinationUnsupportedException"/>/<see cref="DeviceAdapterUnavailableException"/>
/// (which mean the device itself could not be reached at all).</summary>
public sealed class RemoteJobNotSupportedException(string deviceId, RemoteJobType jobType)
    : Exception($"Device '{deviceId}' does not support remote job '{jobType}'.")
{
    public string DeviceId { get; } = deviceId;
    public RemoteJobType JobType { get; } = jobType;
}

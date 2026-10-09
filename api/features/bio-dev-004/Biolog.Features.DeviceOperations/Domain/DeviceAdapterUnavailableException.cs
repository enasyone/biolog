namespace Biolog.Features.DeviceOperations;

/// <summary>BIO-DEV-004-AC04: the combination IS certified, but BIO-DEV-003 has no
/// <c>IDeviceAdapter</c> implementation registered for its vendor/model - a genuine
/// implementation gap, distinct from an uncertified combination
/// (<see cref="DeviceCombinationUnsupportedException"/>). Mirrors BIO-DEV-003's own
/// <c>DeviceAdapterNotAvailableException</c>.</summary>
public sealed class DeviceAdapterUnavailableException(string vendor, string model)
    : Exception($"No adapter is registered for {vendor}/{model}.")
{
}

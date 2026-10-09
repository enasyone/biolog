namespace Biolog.Features.DeviceAdapters;

/// <summary>Thrown when a combination IS certified/supported (the AC03 gate passed) but no
/// <see cref="IDeviceAdapter"/> implementation is registered for its vendor/model - a genuine
/// implementation gap, distinct from an uncertified combination
/// (<see cref="UnsupportedDeviceCombinationException"/>).</summary>
public sealed class DeviceAdapterNotAvailableException(DeviceDescriptor descriptor)
    : Exception($"No adapter is registered for {descriptor.Vendor}/{descriptor.Model}.")
{
    public DeviceDescriptor Descriptor { get; } = descriptor;
}

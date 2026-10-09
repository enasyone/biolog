namespace Biolog.Features.DeviceAdapters;

/// <summary>Resolves the one <see cref="IDeviceAdapter"/> (if any) that declares it
/// <see cref="IDeviceAdapter.Supports"/> a given <see cref="DeviceDescriptor"/>. <c>null</c>
/// means "no adapter implementation exists for this vendor/model" - a genuinely different
/// condition from "this combination is not certified" (see
/// <see cref="DeviceAdapterNotAvailableException"/> vs.
/// <see cref="UnsupportedDeviceCombinationException"/>).</summary>
public interface IDeviceAdapterRegistry
{
    IDeviceAdapter? Resolve(DeviceDescriptor descriptor);
}

/// <summary>BIO-DEV-003-AC01: the registry itself never touches a vendor SDK type - it only
/// asks each registered <see cref="IDeviceAdapter"/> whether it
/// <see cref="IDeviceAdapter.Supports"/> the descriptor. Registered as a singleton over
/// singleton adapters (see <c>BioDev003Module</c>); first match wins, and adapters are
/// expected (not enforced here) to have disjoint vendor/model coverage.</summary>
public sealed class DeviceAdapterRegistry(IEnumerable<IDeviceAdapter> adapters) : IDeviceAdapterRegistry
{
    private readonly IReadOnlyList<IDeviceAdapter> _adapters = adapters.ToList();

    public IDeviceAdapter? Resolve(DeviceDescriptor descriptor) =>
        _adapters.FirstOrDefault(adapter => adapter.Supports(descriptor));
}

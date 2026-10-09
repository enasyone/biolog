namespace Biolog.Features.DeviceAdapters;

/// <summary>
/// The vendor adapter framework's single entry point (BIO-DEV-003-AC02/AC03). Every call
/// checks <see cref="ICertificationGate"/> BEFORE resolving or invoking a vendor adapter -
/// AC03's "unsupported firmware/SDK combinations surface certification status" means a
/// caller must never even reach a vendor SDK simulator for a combination nothing has
/// certified, not merely that the vendor call itself might fail. Only once the gate passes
/// does <see cref="IDeviceAdapterRegistry"/> resolve a concrete adapter, and only that
/// adapter ever touches a vendor SDK type (AC01/AC04).
/// </summary>
public sealed class DeviceAdapterService(IDeviceAdapterRegistry registry, ICertificationGate certificationGate)
{
    public Task<DeviceCapabilities> DiscoverCapabilitiesAsync(DeviceDescriptor descriptor, CancellationToken cancellationToken) =>
        GateThenInvokeAsync(descriptor, (adapter, ct) => adapter.DiscoverCapabilitiesAsync(descriptor, ct), cancellationToken);

    public Task<DeviceHealth> CheckHealthAsync(DeviceDescriptor descriptor, CancellationToken cancellationToken) =>
        GateThenInvokeAsync(descriptor, (adapter, ct) => adapter.CheckHealthAsync(descriptor, ct), cancellationToken);

    private async Task<TResult> GateThenInvokeAsync<TResult>(
        DeviceDescriptor descriptor,
        Func<IDeviceAdapter, CancellationToken, Task<TResult>> invoke,
        CancellationToken cancellationToken)
    {
        var gate = await certificationGate.CheckAsync(descriptor, cancellationToken);
        if (!gate.IsSupported)
        {
            throw new UnsupportedDeviceCombinationException(descriptor, gate.StatusLabel, gate.Detail);
        }

        var adapter = registry.Resolve(descriptor) ?? throw new DeviceAdapterNotAvailableException(descriptor);
        return await invoke(adapter, cancellationToken);
    }
}

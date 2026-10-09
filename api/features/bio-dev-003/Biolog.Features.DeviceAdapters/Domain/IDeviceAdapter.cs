namespace Biolog.Features.DeviceAdapters;

/// <summary>
/// BIO-DEV-003-AC01: "Vendor SDK code is isolated behind typed interfaces." Every concrete
/// implementation (see <c>Adapters/**</c>) wraps exactly one vendor's simulated SDK client
/// (see <c>VendorSdks/**</c>, all <c>internal</c>) and translates its vendor-shaped responses
/// into this project's own domain types (<see cref="DeviceCapabilities"/>,
/// <see cref="DeviceHealth"/>) before returning - nothing outside the adapter's own method
/// body ever sees the vendor SDK type (AC04, also compiler-enforced: <c>VendorSdks</c> types
/// are <c>internal</c>, so a public method cannot declare one as a parameter or return type
/// without a build error).
///
/// <see cref="Supports"/> is the routing predicate <see cref="IDeviceAdapterRegistry"/> uses
/// to find the right adapter for a <see cref="DeviceDescriptor"/> - vendor+model, not
/// firmware+sdk: a real vendor's SDK client typically speaks to every firmware/sdk revision
/// of its own model line (BIO-DEV-003-AC03's "unsupported firmware/SDK combinations" gate is
/// a separate, certification-matrix concern - see <see cref="ICertificationGate"/> - not
/// something this interface itself decides).
/// </summary>
public interface IDeviceAdapter
{
    DeviceCategory Category { get; }

    bool Supports(DeviceDescriptor descriptor);

    Task<DeviceCapabilities> DiscoverCapabilitiesAsync(DeviceDescriptor descriptor, CancellationToken cancellationToken);

    Task<DeviceHealth> CheckHealthAsync(DeviceDescriptor descriptor, CancellationToken cancellationToken);
}

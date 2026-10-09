using Biolog.Features.DeviceAdapters.VendorSdks;

namespace Biolog.Features.DeviceAdapters.Adapters;

/// <summary>
/// BIO-DEV-003-AC01: the ONLY place <see cref="AcmeFingerprintSdkClient"/> (a vendor-shaped
/// type) is ever constructed or called. Its <see cref="AcmeCapabilityManifest"/>/
/// <see cref="AcmeLinkStatus"/> results are translated into this project's own
/// <see cref="DeviceCapabilities"/>/<see cref="DeviceHealth"/> before either method returns -
/// no method on this class exposes a vendor SDK type in its own signature (AC04), and every
/// call is synchronous C# wrapped in <see cref="Task.FromResult{TResult}"/>, matching how a
/// real synchronous vendor SDK call would be wrapped at this exact boundary.
///
/// Internal: routed to only through <see cref="IDeviceAdapterRegistry"/>, never constructed
/// directly by a caller outside this project (see the .csproj's own
/// <c>InternalsVisibleTo</c> comment for how <c>BioDev003Module</c> still registers it into
/// DI despite that).
/// </summary>
internal sealed class AcmeFingerprintScannerAdapter : IDeviceAdapter
{
    public DeviceCategory Category => DeviceCategory.FingerprintScanner;

    public bool Supports(DeviceDescriptor descriptor) =>
        string.Equals(descriptor.Vendor, "Acme", StringComparison.OrdinalIgnoreCase);

    public Task<DeviceCapabilities> DiscoverCapabilitiesAsync(DeviceDescriptor descriptor, CancellationToken cancellationToken)
    {
        var manifest = new AcmeFingerprintSdkClient(descriptor.Serial ?? descriptor.Model).QueryManifest();

        var operations = new List<DeviceOperation>();
        if (manifest.SupportsCapture)
        {
            operations.Add(DeviceOperation.CaptureFingerprint);
        }
        if (manifest.SupportsVerify)
        {
            operations.Add(DeviceOperation.VerifyFingerprint);
        }
        if (manifest.SupportsLivenessCheck)
        {
            operations.Add(DeviceOperation.LivenessCheck);
        }

        return Task.FromResult(new DeviceCapabilities(Category, operations, DateTimeOffset.UtcNow));
    }

    public Task<DeviceHealth> CheckHealthAsync(DeviceDescriptor descriptor, CancellationToken cancellationToken)
    {
        var link = new AcmeFingerprintSdkClient(descriptor.Serial ?? descriptor.Model).Ping();
        var now = DateTimeOffset.UtcNow;

        if (!link.Reachable)
        {
            return Task.FromResult(new DeviceHealth(DeviceHealthStatus.Offline, null, link.Note, now));
        }

        var status = link.RoundTripMs is > 200 ? DeviceHealthStatus.Degraded : DeviceHealthStatus.Online;
        return Task.FromResult(new DeviceHealth(
            status, TimeSpan.FromMilliseconds(link.RoundTripMs!.Value), link.Note, now));
    }
}

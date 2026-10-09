using Biolog.Features.DeviceAdapters.VendorSdks;

namespace Biolog.Features.DeviceAdapters.Adapters;

/// <summary>NFC/UHF reader adapter (BIO-DEV-003-AC01). See
/// <see cref="AcmeFingerprintScannerAdapter"/>'s own header for the isolation shape this
/// mirrors line-for-line, applied to <see cref="OrbitReaderSdk"/> instead.</summary>
internal sealed class OrbitIdNfcUhfReaderAdapter : IDeviceAdapter
{
    public DeviceCategory Category => DeviceCategory.NfcUhfReader;

    public bool Supports(DeviceDescriptor descriptor) =>
        string.Equals(descriptor.Vendor, "OrbitID", StringComparison.OrdinalIgnoreCase);

    public Task<DeviceCapabilities> DiscoverCapabilitiesAsync(DeviceDescriptor descriptor, CancellationToken cancellationToken)
    {
        var protocols = new OrbitReaderSdk(descriptor.Serial ?? descriptor.Model).GetProtocolSupport();

        var operations = new List<DeviceOperation> { DeviceOperation.ReadTag };
        if (protocols.Nfc || protocols.Uhf)
        {
            operations.Add(DeviceOperation.WriteTag);
            operations.Add(DeviceOperation.EncodeTag);
        }

        return Task.FromResult(new DeviceCapabilities(Category, operations, DateTimeOffset.UtcNow));
    }

    public Task<DeviceHealth> CheckHealthAsync(DeviceDescriptor descriptor, CancellationToken cancellationToken)
    {
        var link = new OrbitReaderSdk(descriptor.Serial ?? descriptor.Model).QueryLink();
        var now = DateTimeOffset.UtcNow;

        if (!link.Up)
        {
            return Task.FromResult(new DeviceHealth(DeviceHealthStatus.Offline, null, link.Fault, now));
        }

        var status = link.PollMs is > 200 ? DeviceHealthStatus.Degraded : DeviceHealthStatus.Online;
        return Task.FromResult(new DeviceHealth(
            status, TimeSpan.FromMilliseconds(link.PollMs!.Value), link.Fault, now));
    }
}

using Biolog.Features.DeviceAdapters.VendorSdks;

namespace Biolog.Features.DeviceAdapters.Adapters;

/// <summary>Face-terminal adapter (BIO-DEV-003-AC01). See
/// <see cref="AcmeFingerprintScannerAdapter"/>'s own header for the isolation shape this
/// mirrors line-for-line, applied to <see cref="NovaVisionTerminalClient"/> instead.</summary>
internal sealed class NovaVisionFaceTerminalAdapter : IDeviceAdapter
{
    public DeviceCategory Category => DeviceCategory.FaceTerminal;

    public bool Supports(DeviceDescriptor descriptor) =>
        string.Equals(descriptor.Vendor, "NovaVision", StringComparison.OrdinalIgnoreCase);

    public Task<DeviceCapabilities> DiscoverCapabilitiesAsync(DeviceDescriptor descriptor, CancellationToken cancellationToken)
    {
        var features = new NovaVisionTerminalClient(descriptor.Serial ?? descriptor.Model).GetFeatureSet();

        var operations = new List<DeviceOperation>();
        if (features.Detection)
        {
            operations.Add(DeviceOperation.DetectFace);
        }
        if (features.Matching)
        {
            operations.Add(DeviceOperation.MatchFace);
        }
        if (features.Liveness)
        {
            operations.Add(DeviceOperation.LivenessCheck);
        }

        return Task.FromResult(new DeviceCapabilities(Category, operations, DateTimeOffset.UtcNow));
    }

    public Task<DeviceHealth> CheckHealthAsync(DeviceDescriptor descriptor, CancellationToken cancellationToken)
    {
        var heartbeat = new NovaVisionTerminalClient(descriptor.Serial ?? descriptor.Model).SendHeartbeat();
        var now = DateTimeOffset.UtcNow;

        if (!heartbeat.Alive)
        {
            return Task.FromResult(new DeviceHealth(DeviceHealthStatus.Offline, null, heartbeat.Message, now));
        }

        var status = heartbeat.LatencyMs is > 200 ? DeviceHealthStatus.Degraded : DeviceHealthStatus.Online;
        return Task.FromResult(new DeviceHealth(
            status, TimeSpan.FromMilliseconds(heartbeat.LatencyMs!.Value), heartbeat.Message, now));
    }
}

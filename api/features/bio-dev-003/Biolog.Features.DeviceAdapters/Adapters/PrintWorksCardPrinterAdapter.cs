using Biolog.Features.DeviceAdapters.VendorSdks;

namespace Biolog.Features.DeviceAdapters.Adapters;

/// <summary>Card-printer adapter (BIO-DEV-003-AC01). See
/// <see cref="AcmeFingerprintScannerAdapter"/>'s own header for the isolation shape this
/// mirrors line-for-line, applied to <see cref="PrintWorksDriver"/> instead.</summary>
internal sealed class PrintWorksCardPrinterAdapter : IDeviceAdapter
{
    public DeviceCategory Category => DeviceCategory.CardPrinter;

    public bool Supports(DeviceDescriptor descriptor) =>
        string.Equals(descriptor.Vendor, "PrintWorks", StringComparison.OrdinalIgnoreCase);

    public Task<DeviceCapabilities> DiscoverCapabilitiesAsync(DeviceDescriptor descriptor, CancellationToken cancellationToken)
    {
        var jobs = new PrintWorksDriver(descriptor.Serial ?? descriptor.Model).GetJobCapabilities();

        var operations = new List<DeviceOperation>();
        if (jobs.Print)
        {
            operations.Add(DeviceOperation.PrintCard);
        }
        if (jobs.Encode)
        {
            operations.Add(DeviceOperation.EncodeCard);
        }

        return Task.FromResult(new DeviceCapabilities(Category, operations, DateTimeOffset.UtcNow));
    }

    public Task<DeviceHealth> CheckHealthAsync(DeviceDescriptor descriptor, CancellationToken cancellationToken)
    {
        var report = new PrintWorksDriver(descriptor.Serial ?? descriptor.Model).GetStatusReport();
        var now = DateTimeOffset.UtcNow;

        if (!report.Ready)
        {
            return Task.FromResult(new DeviceHealth(DeviceHealthStatus.Offline, null, report.RibbonNote, now));
        }

        var status = report.ResponseMs is > 200 ? DeviceHealthStatus.Degraded : DeviceHealthStatus.Online;
        return Task.FromResult(new DeviceHealth(
            status, TimeSpan.FromMilliseconds(report.ResponseMs!.Value), report.RibbonNote, now));
    }
}

using Biolog.Features.DeviceAdapters;
using Biolog.Features.DeviceAdapters.Adapters;
using Xunit;

namespace Biolog.Features.DeviceAdapters.Tests;

/// <summary>BIO-DEV-003-AC01: each of the four device kinds named in the user story resolves
/// to its own typed adapter, and an unknown vendor resolves to none.</summary>
public sealed class DeviceAdapterRegistryTests
{
    private static readonly IDeviceAdapter[] AllAdapters =
    [
        new AcmeFingerprintScannerAdapter(),
        new NovaVisionFaceTerminalAdapter(),
        new OrbitIdNfcUhfReaderAdapter(),
        new PrintWorksCardPrinterAdapter(),
    ];

    private static DeviceAdapterRegistry Registry() => new(AllAdapters);

    [Theory]
    [InlineData("Acme", typeof(AcmeFingerprintScannerAdapter), DeviceCategory.FingerprintScanner)]
    [InlineData("NovaVision", typeof(NovaVisionFaceTerminalAdapter), DeviceCategory.FaceTerminal)]
    [InlineData("OrbitID", typeof(OrbitIdNfcUhfReaderAdapter), DeviceCategory.NfcUhfReader)]
    [InlineData("PrintWorks", typeof(PrintWorksCardPrinterAdapter), DeviceCategory.CardPrinter)]
    public void AC01_resolves_the_matching_typed_adapter_for_each_named_device_kind(
        string vendor, Type expectedAdapterType, DeviceCategory expectedCategory)
    {
        var descriptor = new DeviceDescriptor(vendor, "AnyModel", "1.0.0", "sdk-1");

        var adapter = Registry().Resolve(descriptor);

        Assert.NotNull(adapter);
        Assert.IsType(expectedAdapterType, adapter);
        Assert.Equal(expectedCategory, adapter!.Category);
    }

    [Fact]
    public void AC01_an_unknown_vendor_resolves_to_no_adapter()
    {
        var descriptor = new DeviceDescriptor("UnknownVendor", "Model-1", "1.0.0", "sdk-1");

        Assert.Null(Registry().Resolve(descriptor));
    }

    [Fact]
    public void AC01_vendor_matching_is_case_insensitive()
    {
        var descriptor = new DeviceDescriptor("acme", "AnyModel", "1.0.0", "sdk-1");

        Assert.IsType<AcmeFingerprintScannerAdapter>(Registry().Resolve(descriptor));
    }
}

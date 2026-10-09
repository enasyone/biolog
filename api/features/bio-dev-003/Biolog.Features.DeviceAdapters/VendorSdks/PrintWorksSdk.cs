namespace Biolog.Features.DeviceAdapters.VendorSdks;

/// <summary>Card-printer vendor SDK simulator (see <c>AcmeFingerprintSdk.cs</c>'s own header
/// for why this is a simulator, not a real vendor dependency). Internal, referenced only from
/// <c>Adapters/PrintWorksCardPrinterAdapter.cs</c>.</summary>
internal sealed class PrintWorksDriver(string serial)
{
    public PrintWorksJobCapabilities GetJobCapabilities() => new(Print: true, Encode: true);

    public PrintWorksStatusReport GetStatusReport() => serial.Contains("OFFLINE", StringComparison.OrdinalIgnoreCase)
        ? new PrintWorksStatusReport(Ready: false, ResponseMs: null, RibbonNote: "printer offline")
        : serial.Contains("DEGRADED", StringComparison.OrdinalIgnoreCase)
            ? new PrintWorksStatusReport(Ready: true, ResponseMs: 900, RibbonNote: "ribbon low")
            : new PrintWorksStatusReport(Ready: true, ResponseMs: 30, RibbonNote: null);
}

internal sealed record PrintWorksJobCapabilities(bool Print, bool Encode);

internal sealed record PrintWorksStatusReport(bool Ready, int? ResponseMs, string? RibbonNote);

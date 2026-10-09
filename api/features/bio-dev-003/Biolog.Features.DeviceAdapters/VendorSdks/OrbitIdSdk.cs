namespace Biolog.Features.DeviceAdapters.VendorSdks;

/// <summary>NFC/UHF reader vendor SDK simulator (see <c>AcmeFingerprintSdk.cs</c>'s own header
/// for why this is a simulator, not a real vendor dependency). Internal, referenced only from
/// <c>Adapters/OrbitIdNfcUhfReaderAdapter.cs</c>.</summary>
internal sealed class OrbitReaderSdk(string serial)
{
    public OrbitProtocolSupport GetProtocolSupport() => new(Nfc: true, Uhf: true);

    public OrbitLinkState QueryLink() => serial.Contains("OFFLINE", StringComparison.OrdinalIgnoreCase)
        ? new OrbitLinkState(Up: false, PollMs: null, Fault: "antenna link down")
        : serial.Contains("DEGRADED", StringComparison.OrdinalIgnoreCase)
            ? new OrbitLinkState(Up: true, PollMs: 350, Fault: "intermittent read range")
            : new OrbitLinkState(Up: true, PollMs: 8, Fault: null);
}

internal sealed record OrbitProtocolSupport(bool Nfc, bool Uhf);

internal sealed record OrbitLinkState(bool Up, int? PollMs, string? Fault);

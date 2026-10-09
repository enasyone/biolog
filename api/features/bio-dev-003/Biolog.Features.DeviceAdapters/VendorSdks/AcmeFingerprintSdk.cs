namespace Biolog.Features.DeviceAdapters.VendorSdks;

/// <summary>
/// A stand-in for a real fingerprint-scanner vendor's own SDK client shape - this codebase has
/// no real vendor SDK dependency to integrate with (synthetic-data-only, no proprietary
/// hardware in this environment), so this is a deterministic, in-process simulator with a
/// deliberately vendor-specific shape (its own type/method/field names), the same
/// "the framework is real, the hardware integration itself is necessarily simulated" pattern
/// <c>Features.DeviceRegistry.InMemoryDeviceRepository</c> already established for storage.
///
/// Internal, and never referenced outside <c>Adapters/AcmeFingerprintScannerAdapter.cs</c> -
/// BIO-DEV-003-AC04's isolation boundary (nothing outside that one adapter class, in this
/// same project, ever sees this vendor-shaped type; the C# compiler refuses to compile a
/// public member anywhere that exposes it).
/// </summary>
internal sealed class AcmeFingerprintSdkClient(string serial)
{
    public AcmeCapabilityManifest QueryManifest() => new(
        SupportsCapture: true,
        SupportsVerify: true,
        SupportsLivenessCheck: !serial.Contains("NOLIVENESS", StringComparison.OrdinalIgnoreCase));

    public AcmeLinkStatus Ping() => serial.Contains("OFFLINE", StringComparison.OrdinalIgnoreCase)
        ? new AcmeLinkStatus(Reachable: false, RoundTripMs: null, Note: "no response from sensor")
        : serial.Contains("DEGRADED", StringComparison.OrdinalIgnoreCase)
            ? new AcmeLinkStatus(Reachable: true, RoundTripMs: 480, Note: "elevated latency")
            : new AcmeLinkStatus(Reachable: true, RoundTripMs: 12, Note: null);
}

internal sealed record AcmeCapabilityManifest(bool SupportsCapture, bool SupportsVerify, bool SupportsLivenessCheck);

internal sealed record AcmeLinkStatus(bool Reachable, int? RoundTripMs, string? Note);

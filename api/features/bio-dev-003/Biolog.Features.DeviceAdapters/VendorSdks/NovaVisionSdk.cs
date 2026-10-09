namespace Biolog.Features.DeviceAdapters.VendorSdks;

/// <summary>Face-terminal vendor SDK simulator (see <c>AcmeFingerprintSdk.cs</c>'s own header
/// for why this is a simulator, not a real vendor dependency). Internal, referenced only from
/// <c>Adapters/NovaVisionFaceTerminalAdapter.cs</c>.</summary>
internal sealed class NovaVisionTerminalClient(string serial)
{
    public NovaFeatureSet GetFeatureSet() => new(
        Detection: true,
        Matching: true,
        Liveness: !serial.Contains("NOLIVENESS", StringComparison.OrdinalIgnoreCase));

    public NovaHeartbeat SendHeartbeat() => serial.Contains("OFFLINE", StringComparison.OrdinalIgnoreCase)
        ? new NovaHeartbeat(Alive: false, LatencyMs: null, Message: "terminal unreachable")
        : serial.Contains("DEGRADED", StringComparison.OrdinalIgnoreCase)
            ? new NovaHeartbeat(Alive: true, LatencyMs: 610, Message: "camera warm-up degraded")
            : new NovaHeartbeat(Alive: true, LatencyMs: 20, Message: null);
}

internal sealed record NovaFeatureSet(bool Detection, bool Matching, bool Liveness);

internal sealed record NovaHeartbeat(bool Alive, int? LatencyMs, string? Message);

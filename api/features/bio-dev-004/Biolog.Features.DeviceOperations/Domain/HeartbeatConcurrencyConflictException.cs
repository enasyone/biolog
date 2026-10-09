namespace Biolog.Features.DeviceOperations;

/// <summary>Mirrors <c>Features.DeviceRegistry.DeviceConcurrencyConflictException</c>:
/// the device's stored version changed between this poll's read and its write (a concurrent
/// writer, or a concurrent poll of the SAME device). Carries the current version so a
/// caller can retry against it.</summary>
public sealed class HeartbeatConcurrencyConflictException(int currentVersion)
    : Exception($"expected_version did not match the stored version ({currentVersion}); machine code: concurrency_conflict")
{
    public int CurrentVersion { get; } = currentVersion;
}

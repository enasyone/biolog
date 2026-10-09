namespace Biolog.Api.Features.DeviceRegistry;

/// <summary>ADR-0001 Decision 3.2: a mutating write's <c>expected_version</c> no longer
/// matches the stored version. Carries the current version so the caller can retry
/// against it, per the ADR's required response shape.</summary>
public sealed class DeviceConcurrencyConflictException(int currentVersion) : Exception(
    $"expected_version did not match the stored version ({currentVersion}); machine code: concurrency_conflict")
{
    public int CurrentVersion { get; } = currentVersion;
}

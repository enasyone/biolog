namespace Biolog.Features.FaceEnrolment.Liveness;

/// <summary>BIO-BIO-003-AC05: the outcome of one liveness check. <see cref="ProviderId"/> names
/// which hook implementation produced this, mirroring BIO-BIO-001's
/// <c>BiometricProviderHealth.ProviderId</c> - a caller can tell which vendor answered without
/// this feature ever hard-coding that vendor's name anywhere outside its own adapter.</summary>
public sealed record LivenessResult
{
    public required bool IsLive { get; init; }
    public required double Score { get; init; }
    public required string ProviderId { get; init; }
}

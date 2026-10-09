using Biolog.Features.Biometrics.Providers;
using Biolog.Features.FaceEnrolment.Liveness;

namespace Biolog.Features.FaceEnrolment.Tests;

/// <summary>Test double for <see cref="ILivenessDetector"/> with a caller-controlled verdict -
/// keeps <see cref="FaceEnrolmentServiceTests"/> focused on orchestration rather than
/// <see cref="SyntheticLivenessDetector"/>'s hash-based encoding (covered directly by
/// <see cref="SyntheticLivenessDetectorTests"/>).</summary>
public sealed class FakeLivenessDetector(bool isLive) : ILivenessDetector
{
    public string ProviderId => "fake-liveness-v1";

    public Task<LivenessResult> DetectAsync(BiometricSample sample, CancellationToken cancellationToken) =>
        Task.FromResult(new LivenessResult { IsLive = isLive, Score = isLive ? 1.0 : 0.0, ProviderId = ProviderId });
}

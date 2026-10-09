using System.Security.Cryptography;
using Biolog.Features.Biometrics.Providers;

namespace Biolog.Features.FaceEnrolment.Liveness;

/// <summary>
/// Deterministic, offline-safe stand-in <see cref="ILivenessDetector"/> - no real anti-spoofing
/// model, consistent with this story's own "use synthetic data only" constraint and
/// BIO-BIO-001-AC05's identical local-development precedent. Deems a sample "live" when its
/// SHA-256 digest's first byte is even - an arbitrary, deterministic, offline rule with NO
/// accuracy claim, only enough to make the enrolment flow's liveness gate exercisable in tests
/// without a real vendor SDK.
/// </summary>
public sealed class SyntheticLivenessDetector : ILivenessDetector
{
    public string ProviderId => "synthetic-liveness-v1";

    public Task<LivenessResult> DetectAsync(BiometricSample sample, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sample);
        var payload = sample.Payload;
        if (payload.Length == 0)
        {
            return Task.FromResult(new LivenessResult { IsLive = false, Score = 0.0, ProviderId = ProviderId });
        }

        var digest = SHA256.HashData(payload);
        var isLive = digest[0] % 2 == 0;
        return Task.FromResult(new LivenessResult { IsLive = isLive, Score = isLive ? 1.0 : 0.0, ProviderId = ProviderId });
    }
}

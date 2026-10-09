using Biolog.Features.Biometrics.Providers;

namespace Biolog.Features.FaceEnrolment.Liveness;

/// <summary>
/// BIO-BIO-003-AC05: "Liveness hook is supported without hard-coding a vendor." This IS the
/// hook - the ONLY place <see cref="Service.FaceEnrolmentService"/> reaches for a liveness
/// verdict. This story ships only <see cref="SyntheticLivenessDetector"/> (no real anti-spoofing
/// model exists anywhere in this repo); a later story swaps in a real vendor SDK adapter behind
/// this same interface without <see cref="Service.FaceEnrolmentService"/> changing - the same
/// interface-first separation BIO-BIO-001-AC03 established for matching and BIO-BIO-002-AC02
/// established for quality scoring.
/// </summary>
public interface ILivenessDetector
{
    /// <summary>Stable identifier for this detector implementation (never a vendor name baked
    /// into calling code - see <see cref="LivenessResult.ProviderId"/>).</summary>
    string ProviderId { get; }

    Task<LivenessResult> DetectAsync(BiometricSample sample, CancellationToken cancellationToken);
}

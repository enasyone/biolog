using Biolog.Features.Biometrics.Providers;
using Biolog.Features.FaceEnrolment.Domain;

namespace Biolog.Features.FaceEnrolment.Quality;

/// <summary>
/// BIO-BIO-003-AC01/AC02/AC04: a deterministic, offline-safe stand-in <see cref="IFaceQualityChecker"/>
/// - no real face-detection or image-quality model, consistent with this story's own "use
/// synthetic data only" constraint and BIO-BIO-001-AC05's identical local-development precedent.
///
/// <b>Synthetic encoding (test/local-dev only, NOT a real algorithm):</b> <c>payload[0]</c> is
/// read directly as the detected face count (0 = no face, 1 = the happy path, 2+ = multiple
/// faces). <c>payload[1]</c>, if present, is a bit flag byte: bit <c>n</c> set (1) means the
/// <see cref="FaceQualityCheck"/> at ordinal <c>n</c> FAILS; a clear bit (0) means it passes. A
/// payload shorter than 2 bytes is treated as "every check passes" - only face count varies. This
/// makes every AC02/AC04 failure mode deterministically constructible in a test without any real
/// vision model.
/// </summary>
public sealed class SyntheticFaceQualityChecker : IFaceQualityChecker
{
    private static readonly IReadOnlyList<FaceQualityCheck> AllChecks =
        Enum.GetValues<FaceQualityCheck>();

    public Task<FaceQualityAssessment> AssessAsync(BiometricSample sample, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sample);
        var payload = sample.Payload;

        var faceCount = payload.Length == 0 ? 0 : payload[0];
        var failureFlags = payload.Length >= 2 ? payload[1] : (byte)0;

        var checks = AllChecks.Select(check =>
        {
            var bit = 1 << (int)check;
            var failed = (failureFlags & bit) != 0;
            return failed
                ? FaceQualityCheckResult.Fail(check, $"{check.ToString().ToLowerInvariant()}_check_failed: {RecaptureHint(check)}")
                : FaceQualityCheckResult.Pass(check);
        }).ToList();

        return Task.FromResult(new FaceQualityAssessment { FaceCount = faceCount, Checks = checks });
    }

    private static string RecaptureHint(FaceQualityCheck check) => check switch
    {
        FaceQualityCheck.Pose => "face the camera directly and recapture.",
        FaceQualityCheck.Eyes => "open both eyes and look at the camera, then recapture.",
        FaceQualityCheck.Lighting => "move to a well and evenly lit area and recapture.",
        FaceQualityCheck.Sharpness => "hold still and recapture to avoid motion blur.",
        FaceQualityCheck.Resolution => "move closer to the camera or use a higher-resolution capture.",
        FaceQualityCheck.Occlusion => "remove anything covering the face (mask, hand, hair) and recapture.",
        FaceQualityCheck.Background => "use a plain, uncluttered background and recapture.",
        _ => "recapture.",
    };
}

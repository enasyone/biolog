using Biolog.Features.Biometrics.Providers;
using Biolog.Features.FaceEnrolment.Domain;

namespace Biolog.Features.FaceEnrolment.Quality;

/// <summary>BIO-BIO-003-AC01/AC02: the actual face-detection and quality-check ALGORITHM lives
/// behind this seam, not in <see cref="Service.FaceEnrolmentService"/> - the same
/// interface-first separation BIO-BIO-001-AC03 established for matching. This story ships only
/// <see cref="SyntheticFaceQualityChecker"/>; a later story can implement this against a real
/// vendor face-detection/quality SDK without the service changing.</summary>
public interface IFaceQualityChecker
{
    Task<FaceQualityAssessment> AssessAsync(BiometricSample sample, CancellationToken cancellationToken);
}

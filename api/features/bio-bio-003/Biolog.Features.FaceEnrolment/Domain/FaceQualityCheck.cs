namespace Biolog.Features.FaceEnrolment.Domain;

/// <summary>BIO-BIO-003-AC02: "Pose, eyes, lighting, sharpness, resolution, occlusion and
/// background checks are represented." Exactly these seven named checks - a closed set, not a
/// free-text label, so "which checks does this feature perform" is answered by the type system.
/// AC01's "exactly one face" is deliberately NOT a member here - it is a distinct precondition on
/// <see cref="FaceQualityAssessment.FaceCount"/> itself, checked before these seven even run (a
/// quality check on a face that may not exist, or may be one of several, is meaningless).</summary>
public enum FaceQualityCheck
{
    Pose,
    Eyes,
    Lighting,
    Sharpness,
    Resolution,
    Occlusion,
    Background,
}

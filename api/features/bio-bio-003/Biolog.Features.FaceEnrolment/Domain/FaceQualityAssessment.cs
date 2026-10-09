namespace Biolog.Features.FaceEnrolment.Domain;

/// <summary>
/// BIO-BIO-003: the outcome of running <see cref="Quality.IFaceQualityChecker"/> against one
/// capture. <see cref="FaceCount"/> is AC01's "exactly one face" precondition;
/// <see cref="Checks"/> is AC02's seven named checks, only meaningful when
/// <see cref="FaceCount"/> is exactly 1 (a checker implementation is expected to still run and
/// report them, but a face-count failure alone is enough to block approval regardless of what
/// the seven checks say).
/// </summary>
public sealed record FaceQualityAssessment
{
    public required int FaceCount { get; init; }
    public required IReadOnlyList<FaceQualityCheckResult> Checks { get; init; }

    /// <summary>AC01. True only when exactly one face was detected.</summary>
    public bool FaceCountApproved => FaceCount == 1;

    /// <summary>AC01+AC02 combined: the capture may proceed to template extraction only when
    /// both the face-count precondition and every named quality check pass.</summary>
    public bool IsApproved => FaceCountApproved && Checks.All(c => c.Passed);

    /// <summary>AC04: every actionable reason this capture was not approved, in a stable order
    /// (face-count guidance first, then each failed check in <see cref="Checks"/>'s declared
    /// order). Empty exactly when <see cref="IsApproved"/> is true.</summary>
    public IReadOnlyList<string> RecaptureGuidance
    {
        get
        {
            var guidance = new List<string>();
            if (!FaceCountApproved)
            {
                guidance.Add(FaceCount == 0
                    ? "no_face_detected: position your face in the frame and recapture."
                    : $"multiple_faces_detected ({FaceCount}): ensure only one person is in frame and recapture.");
            }
            guidance.AddRange(Checks.Where(c => !c.Passed).Select(c => c.Guidance!));
            return guidance;
        }
    }
}

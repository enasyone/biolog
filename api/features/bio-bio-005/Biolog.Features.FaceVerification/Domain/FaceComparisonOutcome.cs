namespace Biolog.Features.FaceVerification.Domain;

/// <summary>The raw result of comparing a live capture against the claimed identity's latest
/// approved face reference - independent of <see cref="FaceAuthorizationDecision"/> (AC03: the
/// two are deliberately separate concepts). Recorded on every
/// <see cref="FaceVerificationEvidence"/> regardless of what a later authorization step decides.
/// Mirrors BIO-BIO-004's <c>BiometricComparisonOutcome</c> naming/shape for vocabulary
/// consistency across the BIO-BIO epic.</summary>
public enum FaceComparisonOutcome
{
    /// <summary>The live capture's similarity score met AC02's policy-resolved threshold.</summary>
    Matched,

    /// <summary>Extraction and comparison both completed, but the similarity score did not meet
    /// AC02's policy-resolved threshold.</summary>
    NotMatched,

    /// <summary>The live capture could not be turned into a comparable template at all (e.g. an
    /// empty/corrupt capture, or any other provider/vendor extraction failure) - a distinct
    /// outcome from <see cref="NotMatched"/>, since nothing was ever compared. Never a thrown,
    /// unhandled exception - see <see cref="Service.FaceVerificationService"/>'s remarks
    /// (BIO-BIO-004-F01's lesson applied here from the start).</summary>
    AcquisitionFailed,

    /// <summary>AC04/BIO-BIO-005-QA-01: the resolved reference's <see cref="FaceReferenceSnapshot.SourcePhotoMasterVersion"/>
    /// is no longer the claimed identity's CURRENT photo master version (or the identity has no
    /// current photo master at all) - a photo lifecycle change happened since this reference was
    /// captured. No biometric comparison is attempted, since a match against a superseded photo
    /// would be meaningless; this is the defined, deterministic reject outcome the acceptance
    /// probe found missing.</summary>
    ReferenceStale,
}

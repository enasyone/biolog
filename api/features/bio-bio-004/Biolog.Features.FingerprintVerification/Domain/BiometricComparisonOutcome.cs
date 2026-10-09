namespace Biolog.Features.FingerprintVerification.Domain;

/// <summary>
/// AC03: "No-match and acquisition failure are distinct outcomes." The raw result of comparing a
/// live sample against the claimed identity's one authorised reference (AC01) - independent of
/// <see cref="AccessDecision"/>, which additionally folds in AC04's identity-status gate. Recorded
/// on every <see cref="FingerprintVerificationRecord"/> regardless of what the access decision
/// ends up being (AC02: "score/decision ... are recorded").
/// </summary>
public enum BiometricComparisonOutcome
{
    /// <summary>The live sample matched the claimed identity's authorised reference.</summary>
    Matched,

    /// <summary>Extraction and comparison both completed, but the live sample did not match the
    /// claimed identity's authorised reference.</summary>
    NotMatched,

    /// <summary>The live sample could not be turned into a comparable template at all (e.g. an
    /// empty/corrupt capture) - a distinct outcome from <see cref="NotMatched"/>, since nothing
    /// was ever compared. Never a thrown, unhandled exception - see
    /// <see cref="Service.FingerprintVerificationService"/>'s remarks.</summary>
    AcquisitionFailed,
}

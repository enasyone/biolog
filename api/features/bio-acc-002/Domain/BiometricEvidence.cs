namespace Biolog.Features.AccessDecisions.Domain;

/// <summary>Which biometric factor produced one <see cref="BiometricEvidenceEntry"/> (AC01's "face/fingerprint").</summary>
public enum BiometricModality
{
    Fingerprint,
    Face,
}

public static class BiometricModalityExtensions
{
    public static string ToWireString(this BiometricModality modality) => modality switch
    {
        BiometricModality.Fingerprint => "fingerprint",
        BiometricModality.Face => "face",
        _ => throw new ArgumentOutOfRangeException(nameof(modality), modality, "unreachable: exhaustive switch"),
    };
}

/// <summary>
/// The raw outcome of one biometric comparison attempt - independent of the final access
/// decision, mirroring BIO-BIO-004's own <c>BiometricComparisonOutcome</c>/BIO-BIO-005's own
/// <c>FaceComparisonOutcome</c> vocabulary (AC01: evidence is recorded regardless of what it goes
/// on to influence).
/// </summary>
public enum BiometricComparisonOutcome
{
    Matched,
    NotMatched,

    /// <summary>The live sample could not be turned into a comparable template at all - BIO-BIO-004's/BIO-BIO-005's own distinct outcome for a failed capture, never a thrown exception on their side.</summary>
    AcquisitionFailed,

    /// <summary>Face-only: the resolved reference is no longer the claimed identity's current photo master version (BIO-BIO-005-QA-01) - no comparison was attempted.</summary>
    ReferenceStale,

    /// <summary>
    /// This feature's OWN outcome, not a sibling's: the claimed identity has no authorised
    /// biometric reference enrolled at all for this modality (BIO-BIO-004's/BIO-BIO-005's
    /// <c>NoAuthorisedReference</c> precondition failure, caught at the gateway boundary and
    /// turned into a routine evidence outcome here rather than an unhandled 500 - a reader/gate
    /// legitimately encounters an unenrolled person in production, so this cannot be allowed to
    /// crash the decision endpoint).
    /// </summary>
    NotEnrolled,
}

public static class BiometricComparisonOutcomeExtensions
{
    public static string ToWireString(this BiometricComparisonOutcome outcome) => outcome switch
    {
        BiometricComparisonOutcome.Matched => "matched",
        BiometricComparisonOutcome.NotMatched => "not_matched",
        BiometricComparisonOutcome.AcquisitionFailed => "acquisition_failed",
        BiometricComparisonOutcome.ReferenceStale => "reference_stale",
        BiometricComparisonOutcome.NotEnrolled => "not_enrolled",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "unreachable: exhaustive switch"),
    };
}

/// <summary>
/// One biometric comparison attempt's evidence, exactly as recorded by the owning sibling
/// feature - never a decision (AC01: "face/fingerprint match is evidence, not the final allow
/// decision"). Carried unmodified into <see cref="AccessDecisionEvent.BiometricEvidence"/> for
/// audit/replay transparency (AC01 + AC04).
/// </summary>
public sealed record BiometricEvidenceEntry
{
    public required BiometricModality Modality { get; init; }
    public required BiometricComparisonOutcome ComparisonOutcome { get; init; }
    public double? Score { get; init; }
}

namespace Biolog.Features.FingerprintEnrolment.Domain;

/// <summary>
/// BIO-BIO-002-AC01/AC02: the ten standard NIST/FBI finger positions a tenprint enrolment
/// resolves, and the single position a single-finger enrolment names. "Finger identity" (AC02)
/// is this enum, not a free-text label - a caller cannot invent a finger that does not exist.
/// </summary>
public enum FingerPosition
{
    RightThumb,
    RightIndex,
    RightMiddle,
    RightRing,
    RightLittle,
    LeftThumb,
    LeftIndex,
    LeftMiddle,
    LeftRing,
    LeftLittle,
}

public static class FingerPositionExtensions
{
    /// <summary>All ten positions, in the fixed order a tenprint session tracks them.</summary>
    public static readonly IReadOnlyList<FingerPosition> AllTenPrintPositions =
    [
        FingerPosition.RightThumb, FingerPosition.RightIndex, FingerPosition.RightMiddle, FingerPosition.RightRing, FingerPosition.RightLittle,
        FingerPosition.LeftThumb, FingerPosition.LeftIndex, FingerPosition.LeftMiddle, FingerPosition.LeftRing, FingerPosition.LeftLittle,
    ];
}

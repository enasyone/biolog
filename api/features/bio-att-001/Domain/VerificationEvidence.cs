namespace Biolog.Features.Attendance.Domain;

/// <summary>AC01's "confidence/verification evidence" - a structured, honest statement of how (or whether) the claimed person identity behind an event was checked, never a bare boolean.</summary>
public sealed record VerificationEvidence
{
    public required VerificationMethod Method { get; init; }

    public required bool Verified { get; init; }

    /// <summary>0.0-1.0 confidence score from the verification method (e.g. a biometric match score). <c>null</c> when the method does not produce one (e.g. <see cref="VerificationMethod.Manual"/>) - a genuinely absent measurement, never defaulted to 0 or 1.</summary>
    public double? Confidence { get; init; }

    public static VerificationEvidence Validate(VerificationEvidence evidence)
    {
        if (evidence.Confidence is < 0.0 or > 1.0)
        {
            throw AttendanceError.Validation([new Api.ValidationErrorItem("evidence.confidence", "out_of_range", "confidence must be between 0.0 and 1.0 inclusive")]);
        }

        if (evidence.Method == VerificationMethod.Unverified && evidence.Verified)
        {
            throw AttendanceError.Validation([new Api.ValidationErrorItem("evidence.verified", "inconsistent", "an unverified method cannot itself report verified=true")]);
        }

        return evidence;
    }
}

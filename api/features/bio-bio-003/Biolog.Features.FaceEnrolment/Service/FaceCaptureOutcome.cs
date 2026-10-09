using Biolog.Features.FaceEnrolment.Domain;

namespace Biolog.Features.FaceEnrolment.Service;

/// <summary>
/// BIO-BIO-003-AC04: "poor capture yields actionable recapture guidance" is an ordinary,
/// expected business outcome - not an exception. <see cref="FaceEnrolmentService.CaptureAsync"/>
/// returns this discriminated result instead of throwing for a failed quality/liveness gate,
/// reserving thrown exceptions for genuine errors (a missing source photo, a provider failure).
/// Exactly one of <see cref="Reference"/>/<see cref="RecaptureGuidance"/> is populated.
/// </summary>
public sealed record FaceCaptureOutcome
{
    public required bool Approved { get; init; }
    public FaceBiometricReference? Reference { get; init; }
    public IReadOnlyList<string> RecaptureGuidance { get; init; } = [];

    public static FaceCaptureOutcome Approve(FaceBiometricReference reference) =>
        new() { Approved = true, Reference = reference };

    public static FaceCaptureOutcome Reject(IReadOnlyList<string> guidance) =>
        new() { Approved = false, RecaptureGuidance = guidance };
}

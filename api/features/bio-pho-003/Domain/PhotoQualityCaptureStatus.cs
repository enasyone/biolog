namespace Biolog.Features.PhotoQuality.Domain;

/// <summary>
/// Three terminal-or-pending states, no others. Unlike BIO-PHO-002's own <c>CaptureStatus</c>
/// (whose failing quality gate auto-rejects at submission, so a failed submission never passes
/// through <c>Pending</c>), BIO-PHO-003-AC01/AC03's own workflow is enrolment-staff-driven: a
/// capture ALWAYS starts <see cref="Pending"/> regardless of what either quality assessment
/// says, carrying both as informational signals for a human operator (<see cref="PhotoQualityCapture"/>'s
/// own <c>PrintQualityPassed</c>/<c>BiometricSuitabilityPassed</c>/their reasons) - "a photo can
/// pass print and fail biometric quality, or vice versa" (AC01) describes two independent
/// SIGNALS, not an automatic combined gate. Only an explicit operator decision
/// (<see cref="PhotoQualityService.ApproveAsync"/>/<see cref="PhotoQualityService.RejectAsync"/>)
/// moves a <see cref="Pending"/> capture to <see cref="Rejected"/> or <see cref="Approved"/>,
/// both terminal - see <see cref="IPhotoQualityCaptureRepository"/>'s header for how an illegal
/// transition is structurally refused, not merely convention.
/// </summary>
public enum PhotoQualityCaptureStatus
{
    Pending,
    Rejected,
    Approved,
}

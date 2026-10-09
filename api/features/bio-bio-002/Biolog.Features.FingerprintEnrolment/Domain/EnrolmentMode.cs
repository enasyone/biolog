namespace Biolog.Features.FingerprintEnrolment.Domain;

/// <summary>BIO-BIO-002-AC01: "10-print and single-finger modes exist." A session's mode fixes
/// which <see cref="FingerPosition"/>s it must resolve before it can complete - see
/// <see cref="FingerprintEnrolmentSession.TargetPositions"/>.</summary>
public enum EnrolmentMode
{
    TenPrint,
    SingleFinger,
}

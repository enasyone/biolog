namespace Biolog.Features.FingerprintEnrolment.Domain;

/// <summary>BIO-BIO-002-AC03: a finger's resolution state within a session.
/// <see cref="MissingAmputated"/> is a distinct, PERMANENT resolution (the exception path) -
/// not "not captured yet" (<see cref="Pending"/>) and not a transient capture failure a
/// recapture would fix.</summary>
public enum FingerCaptureStatus
{
    Pending,
    Captured,
    MissingAmputated,
}

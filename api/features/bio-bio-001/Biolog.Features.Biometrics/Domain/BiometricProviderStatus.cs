namespace Biolog.Features.Biometrics.Domain;

/// <summary>
/// BIO-BIO-001-AC04: the health states a provider adapter can report. <see cref="Unavailable"/>
/// is distinct from <see cref="Degraded"/> so a caller can decide whether to fail closed (no
/// enrolment/verification possible) or proceed with a warning (slower/less accurate but
/// functional) - a bare bool would collapse that distinction.
/// </summary>
public enum BiometricProviderStatus
{
    Healthy,
    Degraded,
    Unavailable,
}

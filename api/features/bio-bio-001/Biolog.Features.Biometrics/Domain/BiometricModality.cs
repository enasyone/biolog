namespace Biolog.Features.Biometrics.Domain;

/// <summary>
/// The two biometric modalities BIO-BIO-001-AC01 requires a provider interface for. Deliberately
/// closed (not an open string) so "which modalities does this abstraction support" is answered by
/// the type system, not by convention: a future modality (e.g. iris) is a new enum member plus a
/// new <c>I&lt;Modality&gt;Provider</c> interface, not a free-text value nothing validates.
/// </summary>
public enum BiometricModality
{
    Fingerprint,
    Face,
}

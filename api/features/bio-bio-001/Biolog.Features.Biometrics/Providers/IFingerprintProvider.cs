namespace Biolog.Features.Biometrics.Providers;

/// <summary>BIO-BIO-001-AC01: the fingerprint provider contract. A vendor SDK adapter (e.g. a
/// specific scanner's SDK) implements this directly; nothing outside the adapter needs to know
/// which vendor SDK is behind it.</summary>
public interface IFingerprintProvider : IBiometricProvider
{
    Task<ExtractedBiometricFeatures> ExtractTemplateAsync(BiometricSample sample, CancellationToken cancellationToken);
}

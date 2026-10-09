namespace Biolog.Features.Biometrics.Providers;

/// <summary>BIO-BIO-001-AC01: the face provider contract. A vendor SDK adapter (e.g. a specific
/// face-capture SDK) implements this directly; nothing outside the adapter needs to know which
/// vendor SDK is behind it.</summary>
public interface IFaceProvider : IBiometricProvider
{
    Task<ExtractedBiometricFeatures> ExtractTemplateAsync(BiometricSample sample, CancellationToken cancellationToken);
}

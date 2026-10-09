using System.Security.Cryptography;
using Biolog.Features.Biometrics.Domain;
using Biolog.Features.Biometrics.Providers;

namespace Biolog.Features.Biometrics.Synthetic;

/// <summary>BIO-BIO-001-AC05: the face-modality counterpart of
/// <see cref="SyntheticFingerprintProvider"/> - same deterministic, offline-safe, no-accuracy-
/// claim design, see that class's remarks.</summary>
public sealed class SyntheticFaceProvider : IFaceProvider
{
    public const string Version = "synthetic-face-1.0.0";

    public string ProviderId => "synthetic-face-v1";
    public BiometricModality Modality => BiometricModality.Face;

    public Task<ExtractedBiometricFeatures> ExtractTemplateAsync(BiometricSample sample, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sample);
        if (sample.Payload is null || sample.Payload.Length == 0)
        {
            throw new ArgumentException("biometric_sample_payload_empty", nameof(sample));
        }

        var digest = SHA256.HashData(sample.Payload);
        return Task.FromResult(new ExtractedBiometricFeatures { Payload = digest, ProviderVersion = Version });
    }

    public Task<BiometricProviderHealth> GetHealthAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new BiometricProviderHealth
        {
            ProviderId = ProviderId,
            Modality = Modality,
            Status = BiometricProviderStatus.Healthy,
            ModelVersion = Version,
            CheckedAtUtc = DateTime.UtcNow.ToString("O"),
        });
}

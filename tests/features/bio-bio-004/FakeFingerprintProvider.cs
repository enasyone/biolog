using Biolog.Features.Biometrics.Domain;
using Biolog.Features.Biometrics.Providers;

namespace Biolog.Features.FingerprintVerification.Tests;

/// <summary>BIO-BIO-004-F01 (Seyi round 1, High) regression fixture: a provider whose
/// <see cref="ExtractTemplateAsync"/> throws whatever exception the test configures - proves
/// <see cref="Service.FingerprintVerificationService.VerifyAsync"/> contains every kind of
/// provider/vendor failure, not only <see cref="ArgumentException"/>.</summary>
public sealed class FakeFingerprintProvider(Func<Exception>? throwing = null) : IFingerprintProvider
{
    public string ProviderId => "fake-fingerprint-v1";
    public BiometricModality Modality => BiometricModality.Fingerprint;

    public Task<ExtractedBiometricFeatures> ExtractTemplateAsync(BiometricSample sample, CancellationToken cancellationToken)
    {
        if (throwing is not null)
        {
            throw throwing();
        }

        return Task.FromResult(new ExtractedBiometricFeatures { Payload = sample.Payload, ProviderVersion = "fake-1.0.0" });
    }

    public Task<BiometricProviderHealth> GetHealthAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new BiometricProviderHealth
        {
            ProviderId = ProviderId,
            Modality = Modality,
            Status = BiometricProviderStatus.Healthy,
            ModelVersion = "fake-1.0.0",
            CheckedAtUtc = "2026-01-01T00:00:00Z",
        });
}

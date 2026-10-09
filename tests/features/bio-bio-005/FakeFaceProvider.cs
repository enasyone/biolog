using Biolog.Features.Biometrics.Domain;
using Biolog.Features.Biometrics.Providers;

namespace Biolog.Features.FaceVerification.Tests;

/// <summary>A face provider whose <see cref="ExtractTemplateAsync"/> throws whatever exception
/// the test configures - proves <see cref="Service.FaceVerificationService.CompareAsync"/>
/// contains every kind of provider/vendor failure, not only <see cref="ArgumentException"/>
/// (BIO-BIO-004-F01's lesson, applied here from the start).</summary>
public sealed class FakeFaceProvider(Func<Exception>? throwing = null) : IFaceProvider
{
    public string ProviderId => "fake-face-v1";
    public BiometricModality Modality => BiometricModality.Face;

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

using Biolog.Features.Biometrics.Domain;
using Biolog.Features.Biometrics.Matching;
using Biolog.Features.Biometrics.Service;
using Biolog.Features.Biometrics.Synthetic;
using Xunit;

namespace Biolog.Features.Biometrics.Tests;

/// <summary>BIO-BIO-001-AC04: "Provider health and model/version metadata are supported."</summary>
public sealed class ProviderHealthTests
{
    [Fact]
    public async Task AC04_each_provider_reports_status_and_model_version()
    {
        var fingerprint = new SyntheticFingerprintProvider();
        var face = new SyntheticFaceProvider();

        var fingerprintHealth = await fingerprint.GetHealthAsync(CancellationToken.None);
        var faceHealth = await face.GetHealthAsync(CancellationToken.None);

        Assert.Equal(BiometricProviderStatus.Healthy, fingerprintHealth.Status);
        Assert.Equal(SyntheticFingerprintProvider.Version, fingerprintHealth.ModelVersion);
        Assert.Equal("synthetic-fingerprint-v1", fingerprintHealth.ProviderId);

        Assert.Equal(BiometricProviderStatus.Healthy, faceHealth.Status);
        Assert.Equal(SyntheticFaceProvider.Version, faceHealth.ModelVersion);
        Assert.Equal("synthetic-face-v1", faceHealth.ProviderId);
    }

    [Fact]
    public async Task AC04_BiometricService_aggregates_health_across_both_registered_providers()
    {
        var service = new BiometricService(
            new SyntheticFingerprintProvider(),
            new SyntheticFaceProvider(),
            new InMemoryBiometricTemplateStore(),
            new SyntheticBiometricMatcher());

        var health = await service.GetProviderHealthAsync(CancellationToken.None);

        Assert.Equal(2, health.Count);
        Assert.Contains(health, h => h.Modality == BiometricModality.Fingerprint);
        Assert.Contains(health, h => h.Modality == BiometricModality.Face);
    }
}

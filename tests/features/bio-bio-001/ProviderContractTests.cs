using Biolog.Features.Biometrics.Domain;
using Biolog.Features.Biometrics.Providers;
using Biolog.Features.Biometrics.Synthetic;
using Xunit;

namespace Biolog.Features.Biometrics.Tests;

/// <summary>BIO-BIO-001-AC01: "Fingerprint and face provider interfaces are defined." Proves two
/// DISTINCT interfaces exist (not one generic interface parameterised by modality) and that each
/// has a working synthetic implementation.</summary>
public sealed class ProviderContractTests
{
    [Fact]
    public void AC01_fingerprint_and_face_provider_interfaces_are_distinct_types()
    {
        Assert.True(typeof(IFingerprintProvider).IsInterface);
        Assert.True(typeof(IFaceProvider).IsInterface);
        Assert.NotEqual(typeof(IFingerprintProvider), typeof(IFaceProvider));
        Assert.True(typeof(IBiometricProvider).IsAssignableFrom(typeof(IFingerprintProvider)));
        Assert.True(typeof(IBiometricProvider).IsAssignableFrom(typeof(IFaceProvider)));
    }

    [Fact]
    public async Task AC01_synthetic_fingerprint_provider_implements_the_interface_and_extracts_a_feature_payload()
    {
        IFingerprintProvider provider = new SyntheticFingerprintProvider();
        Assert.Equal(BiometricModality.Fingerprint, provider.Modality);

        var features = await provider.ExtractTemplateAsync(Sample(new byte[] { 1, 2, 3 }), CancellationToken.None);

        Assert.NotEmpty(features.Payload);
        Assert.Equal(SyntheticFingerprintProvider.Version, features.ProviderVersion);
    }

    [Fact]
    public async Task AC01_synthetic_face_provider_implements_the_interface_and_extracts_a_feature_payload()
    {
        IFaceProvider provider = new SyntheticFaceProvider();
        Assert.Equal(BiometricModality.Face, provider.Modality);

        var features = await provider.ExtractTemplateAsync(Sample(new byte[] { 9, 9, 9 }), CancellationToken.None);

        Assert.NotEmpty(features.Payload);
        Assert.Equal(SyntheticFaceProvider.Version, features.ProviderVersion);
    }

    [Fact]
    public async Task Extraction_rejects_an_empty_sample()
    {
        IFingerprintProvider provider = new SyntheticFingerprintProvider();
        await Assert.ThrowsAsync<ArgumentException>(() => provider.ExtractTemplateAsync(Sample(Array.Empty<byte>()), CancellationToken.None));
    }

    [Fact]
    public async Task Identical_samples_extract_to_identical_features_different_samples_do_not()
    {
        IFingerprintProvider provider = new SyntheticFingerprintProvider();
        var a1 = await provider.ExtractTemplateAsync(Sample(new byte[] { 1, 2, 3 }), CancellationToken.None);
        var a2 = await provider.ExtractTemplateAsync(Sample(new byte[] { 1, 2, 3 }), CancellationToken.None);
        var b = await provider.ExtractTemplateAsync(Sample(new byte[] { 4, 5, 6 }), CancellationToken.None);

        Assert.Equal(a1.Payload, a2.Payload);
        Assert.NotEqual(a1.Payload, b.Payload);
    }

    private static BiometricSample Sample(byte[] payload) => new()
    {
        Payload = payload,
        ContentType = "application/octet-stream",
        CapturedAtUtc = "2026-01-01T00:00:00Z",
    };
}

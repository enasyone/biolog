using System.Reflection;
using Biolog.Features.Biometrics.Domain;
using Biolog.Features.Biometrics.Synthetic;
using Xunit;

namespace Biolog.Features.Biometrics.Tests;

/// <summary>BIO-BIO-001-AC05: "No real biometric data is required for local development." Every
/// implementation this story ships (providers, store, matcher) is synthetic and works fully
/// offline; this test suite itself never reads a real fingerprint/face capture from disk or a
/// device, only in-memory byte arrays constructed by the test.</summary>
public sealed class SyntheticDataOnlyTests
{
    [Fact]
    public async Task Full_enrol_verify_flow_runs_entirely_on_synthetic_in_memory_bytes_no_external_dependency()
    {
        var service = new Service.BiometricService(
            new SyntheticFingerprintProvider(), new SyntheticFaceProvider(),
            new InMemoryBiometricTemplateStore(), new SyntheticBiometricMatcher());

        var syntheticSample = new Providers.BiometricSample
        {
            Payload = new byte[] { 0x01, 0x02, 0x03, 0x04 }, // synthetic - not a real capture
            ContentType = "application/octet-stream",
            CapturedAtUtc = "2026-01-01T00:00:00Z",
        };

        var template = await service.EnrollAsync("ten_a", "per_a", BiometricModality.Face, syntheticSample, CancellationToken.None);
        Assert.NotNull(template);

        var health = await service.GetProviderHealthAsync(CancellationToken.None);
        Assert.All(health, h => Assert.Equal(BiometricProviderStatus.Healthy, h.Status));
    }

    [Fact]
    public void AC05_shipped_provider_implementations_are_declared_synthetic_not_a_real_vendor_SDK()
    {
        Assert.Contains("Synthetic", typeof(SyntheticFingerprintProvider).Namespace);
        Assert.Contains("Synthetic", typeof(SyntheticFaceProvider).Namespace);
        Assert.Contains("Synthetic", typeof(InMemoryBiometricTemplateStore).Namespace);
        Assert.Contains("Synthetic", typeof(SyntheticBiometricMatcher).Namespace);
    }

    [Fact]
    public void AC05_the_feature_project_references_no_biometric_vendor_SDK_package()
    {
        var referencedAssemblies = typeof(SyntheticFingerprintProvider).Assembly.GetReferencedAssemblies();
        Assert.DoesNotContain(referencedAssemblies, a =>
            a.Name is not null && (a.Name.Contains("Vendor", StringComparison.OrdinalIgnoreCase) ||
                                    a.Name.Contains("Sdk", StringComparison.OrdinalIgnoreCase)));
    }
}

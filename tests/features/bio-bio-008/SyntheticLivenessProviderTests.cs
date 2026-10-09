using Biolog.Features.Biometrics.Providers;
using Biolog.Features.Liveness.Domain;
using Xunit;

namespace Biolog.Features.Liveness.Tests;

public sealed class SyntheticLivenessProviderTests
{
    private static readonly byte[] KnownLiveDemonstrationSeed = [0x00, 0x00, 0x00, 0x01];

    [Fact]
    public async Task AC01_evidence_carries_provider_method_version_confidence_and_status()
    {
        var provider = new SyntheticLivenessProvider();
        var sample = AdversarialSampleFactory.Genuine(KnownLiveDemonstrationSeed);

        var evidence = await provider.CheckAsync(sample, CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(evidence.ProviderId));
        Assert.False(string.IsNullOrWhiteSpace(evidence.Method));
        Assert.False(string.IsNullOrWhiteSpace(evidence.ProviderVersion));
        Assert.InRange(evidence.Confidence, 0.0, 1.0);
        Assert.True(Enum.IsDefined(evidence.Status));
    }

    [Fact]
    public async Task AC01_the_known_demonstration_seed_deterministically_produces_Live()
    {
        var provider = new SyntheticLivenessProvider();
        var sample = AdversarialSampleFactory.Genuine(KnownLiveDemonstrationSeed);

        var evidence = await provider.CheckAsync(sample, CancellationToken.None);

        Assert.Equal(LivenessStatus.Live, evidence.Status);
    }

    [Fact]
    public async Task AC04_every_named_adversarial_case_is_representable_and_deterministically_recognised()
    {
        foreach (var kind in new[] { AdversarialCaseKind.Replay, AdversarialCaseKind.ScreenPresentation, AdversarialCaseKind.PhotoPresentation })
        {
            var provider = new SyntheticLivenessProvider();
            var sample = AdversarialSampleFactory.Create(kind);

            var evidence = await provider.CheckAsync(sample, CancellationToken.None);

            Assert.Equal(LivenessStatus.SpoofDetected, evidence.Status);
            Assert.Contains(kind.ToString().ToLowerInvariant(), evidence.Reason);
        }
    }

    [Fact]
    public async Task AC04_adversarial_cases_are_reproducible_not_randomised()
    {
        var provider = new SyntheticLivenessProvider();
        var first = await provider.CheckAsync(AdversarialSampleFactory.Create(AdversarialCaseKind.Replay), CancellationToken.None);
        var second = await provider.CheckAsync(AdversarialSampleFactory.Create(AdversarialCaseKind.Replay), CancellationToken.None);

        Assert.Equal(first.Status, second.Status);
        Assert.Equal(first.Reason, second.Reason);
    }

    [Fact]
    public async Task all_three_LivenessStatus_members_are_reachable_from_untagged_genuine_samples()
    {
        var provider = new SyntheticLivenessProvider();
        var observed = new HashSet<LivenessStatus>();
        for (var i = 0; i < 100; i++)
        {
            var sample = AdversarialSampleFactory.Genuine(BitConverter.GetBytes(i));
            var evidence = await provider.CheckAsync(sample, CancellationToken.None);
            observed.Add(evidence.Status);
        }

        Assert.Equal(3, observed.Count);
    }

    [Fact]
    public async Task boundary_an_empty_payload_is_a_genuine_integration_error_not_a_routine_outcome()
    {
        var provider = new SyntheticLivenessProvider();
        var sample = new BiometricSample { Payload = [], ContentType = "application/octet-stream", CapturedAtUtc = "2026-01-01T00:00:00Z" };

        await Assert.ThrowsAsync<LivenessException>(() => provider.CheckAsync(sample, CancellationToken.None));
    }

    [Fact]
    public async Task boundary_a_null_sample_throws_ArgumentNullException()
    {
        var provider = new SyntheticLivenessProvider();

        await Assert.ThrowsAsync<ArgumentNullException>(() => provider.CheckAsync(null!, CancellationToken.None));
    }
}

using Biolog.Features.Biometrics.Providers;
using Biolog.Features.Liveness.Domain;
using Biolog.Features.Liveness.Service;
using Xunit;

namespace Biolog.Features.Liveness.Tests;

public sealed class LivenessEvaluationServiceTests
{
    private static readonly byte[] KnownLiveDemonstrationSeed = [0x00, 0x00, 0x00, 0x01];

    private static LivenessEvaluationService NewService(ILivenessPolicy? policy = null) =>
        new(policy ?? new InMemoryLivenessPolicy(), new SyntheticLivenessProvider());

    [Fact]
    public async Task AC03_IsRequiredAsync_delegates_to_the_policy()
    {
        var policy = new InMemoryLivenessPolicy();
        policy.SetRequired("ten_a", LivenessWorkflows.FaceVerification1to1);
        var service = NewService(policy);

        Assert.True(await service.IsRequiredAsync("ten_a", LivenessWorkflows.FaceVerification1to1, CancellationToken.None));
        Assert.False(await service.IsRequiredAsync("ten_a", LivenessWorkflows.FaceEnrolment, CancellationToken.None));
    }

    [Fact]
    public async Task AC01_EvaluateAsync_returns_real_provider_evidence()
    {
        var service = NewService();

        var evidence = await service.EvaluateAsync(AdversarialSampleFactory.Genuine(KnownLiveDemonstrationSeed), CancellationToken.None);

        Assert.Equal(LivenessStatus.Live, evidence.Status);
        Assert.Equal("synthetic-liveness-v1", evidence.ProviderId);
    }

    [Fact]
    public async Task EvaluateAsync_is_independent_of_IsRequiredAsync_a_caller_can_evaluate_even_when_not_required()
    {
        var service = NewService();

        var required = await service.IsRequiredAsync("ten_a", LivenessWorkflows.FaceEnrolment, CancellationToken.None);
        var evidence = await service.EvaluateAsync(AdversarialSampleFactory.Create(AdversarialCaseKind.Replay), CancellationToken.None);

        Assert.False(required);
        Assert.Equal(LivenessStatus.SpoofDetected, evidence.Status);
    }

    [Fact]
    public async Task boundary_evaluating_an_empty_sample_propagates_the_provider_exception()
    {
        var service = NewService();
        var emptySample = new BiometricSample
        {
            Payload = [],
            ContentType = "application/octet-stream",
            CapturedAtUtc = "2026-01-01T00:00:00Z",
        };

        await Assert.ThrowsAsync<LivenessException>(() => service.EvaluateAsync(emptySample, CancellationToken.None));
    }
}

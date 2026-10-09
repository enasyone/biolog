using Biolog.Features.Biometrics.Providers;
using Biolog.Features.Liveness.Domain;

namespace Biolog.Features.Liveness.Service;

/// <summary>
/// BIO-BIO-008's own orchestration surface: combines <see cref="ILivenessPolicy"/> (AC03 - "is
/// liveness required here") with <see cref="ILivenessProvider"/> (AC01 - "what is the evidence").
/// Deliberately does NOT gate or decide anything itself - a caller that finds liveness required
/// and then evaluates a non-<see cref="LivenessStatus.Live"/> result decides what to do with that
/// on its own; see <see cref="ILivenessPolicy"/>'s own remarks for why.
/// </summary>
internal sealed class LivenessEvaluationService
{
    private readonly ILivenessPolicy _policy;
    private readonly ILivenessProvider _provider;

    public LivenessEvaluationService(ILivenessPolicy policy, ILivenessProvider provider)
    {
        _policy = policy;
        _provider = provider;
    }

    public Task<bool> IsRequiredAsync(string tenantId, string workflow, CancellationToken cancellationToken) =>
        _policy.IsRequiredAsync(tenantId, workflow, cancellationToken);

    public Task<LivenessEvidence> EvaluateAsync(BiometricSample sample, CancellationToken cancellationToken) =>
        _provider.CheckAsync(sample, cancellationToken);
}

using System.Security.Cryptography;
using Biolog.Features.Biometrics.Providers;

namespace Biolog.Features.Liveness.Domain;

/// <summary>
/// Deterministic, offline-safe <see cref="ILivenessProvider"/> - no real anti-spoofing model
/// exists anywhere in this repo, consistent with this story's own "use synthetic data only"
/// constraint and BIO-BIO-003-AC05's identical precedent for its own narrower hook. Recognises
/// <see cref="AdversarialSampleFactory"/>'s tagged samples and answers
/// <see cref="LivenessStatus.SpoofDetected"/> for each (BIO-BIO-008-AC04); an untagged payload is
/// evaluated by a deterministic, arbitrary hash-based rule with NO accuracy claim (the same style
/// BIO-BIO-003's own <c>SyntheticLivenessDetector</c> uses, extended to a three-way Live/
/// Inconclusive/SpoofDetected split so all three <see cref="LivenessStatus"/> members are
/// reachable without an adversarial tag too).
/// </summary>
internal sealed class SyntheticLivenessProvider : ILivenessProvider
{
    public string ProviderId => "synthetic-liveness-v1";
    public string ProviderVersion => "1.0.0";

    public Task<LivenessEvidence> CheckAsync(BiometricSample sample, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sample);
        var payload = sample.Payload;
        if (payload.Length == 0)
        {
            throw LivenessException.EmptySample();
        }

        if (AdversarialSampleFactory.TryRecognise(payload, out var adversarialKind))
        {
            return Task.FromResult(new LivenessEvidence
            {
                ProviderId = ProviderId,
                Method = "passive",
                ProviderVersion = ProviderVersion,
                Confidence = 0.95,
                Status = LivenessStatus.SpoofDetected,
                Reason = $"adversarial_case_{adversarialKind.ToString().ToLowerInvariant()}",
            });
        }

        var digest = SHA256.HashData(payload);
        // Three-way split on the digest's first byte - arbitrary and deterministic only, the
        // same "no accuracy claim" rule BIO-BIO-003's own detector documents for its own
        // two-way split.
        var bucket = digest[0] % 3;
        return Task.FromResult(bucket switch
        {
            0 => new LivenessEvidence { ProviderId = ProviderId, Method = "passive", ProviderVersion = ProviderVersion, Confidence = 0.9, Status = LivenessStatus.Live },
            1 => new LivenessEvidence { ProviderId = ProviderId, Method = "passive", ProviderVersion = ProviderVersion, Confidence = 0.4, Status = LivenessStatus.Inconclusive, Reason = "low_confidence_capture" },
            _ => new LivenessEvidence { ProviderId = ProviderId, Method = "passive", ProviderVersion = ProviderVersion, Confidence = 0.85, Status = LivenessStatus.SpoofDetected, Reason = "presentation_attack_suspected" },
        });
    }
}

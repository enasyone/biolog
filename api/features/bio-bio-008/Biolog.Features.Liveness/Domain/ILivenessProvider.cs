using Biolog.Features.Biometrics.Providers;

namespace Biolog.Features.Liveness.Domain;

/// <summary>
/// BIO-BIO-008-AC01: "provider-neutral". The ONLY place any caller reaches for a liveness
/// verdict - a vendor's real anti-spoofing SDK is a future adapter behind this same interface,
/// exactly the separation BIO-BIO-001-AC03 established for matching and BIO-BIO-003-AC05
/// established for its own (narrower) enrolment-only hook.
/// </summary>
internal interface ILivenessProvider
{
    string ProviderId { get; }
    string ProviderVersion { get; }

    Task<LivenessEvidence> CheckAsync(BiometricSample sample, CancellationToken cancellationToken);
}

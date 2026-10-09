using System.Security.Cryptography;
using Biolog.Features.Biometrics.Domain;
using Biolog.Features.Biometrics.Providers;

namespace Biolog.Features.Biometrics.Synthetic;

/// <summary>
/// BIO-BIO-001-AC05: "No real biometric data is required for local development." A deterministic
/// stand-in <see cref="IFingerprintProvider"/> - it never talks to a real fingerprint scanner or
/// vendor SDK, so local development and this story's own tests run fully offline on synthetic
/// byte payloads. "Extraction" is a SHA-256 hash of the sample payload, which is enough to make
/// identical samples produce identical templates (so a synthetic enroll/verify round-trip is
/// meaningful) without this class making any accuracy claim whatsoever - it is not, and must
/// never be mistaken for, a real fingerprint feature extractor.
///
/// <b>AC02 in practice:</b> this class privately tracks a vendor-style capture counter purely to
/// demonstrate the isolation <see cref="ExtractedBiometricFeatures"/>'s own remarks describe -
/// that counter is never read by anything outside this class and has no field on
/// <see cref="ExtractedBiometricFeatures"/> or <see cref="BiometricTemplate"/> it could be
/// written to even if this class wanted to leak it.
/// </summary>
public sealed class SyntheticFingerprintProvider : IFingerprintProvider
{
    public const string Version = "synthetic-fingerprint-1.0.0";

    private long _internalVendorCaptureCounter;

    public string ProviderId => "synthetic-fingerprint-v1";
    public BiometricModality Modality => BiometricModality.Fingerprint;

    public Task<ExtractedBiometricFeatures> ExtractTemplateAsync(BiometricSample sample, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sample);
        if (sample.Payload is null || sample.Payload.Length == 0)
        {
            throw new ArgumentException("biometric_sample_payload_empty", nameof(sample));
        }

        // Never observable outside this instance - see the AC02 remarks above.
        Interlocked.Increment(ref _internalVendorCaptureCounter);

        var digest = SHA256.HashData(sample.Payload);
        return Task.FromResult(new ExtractedBiometricFeatures { Payload = digest, ProviderVersion = Version });
    }

    public Task<BiometricProviderHealth> GetHealthAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new BiometricProviderHealth
        {
            ProviderId = ProviderId,
            Modality = Modality,
            Status = BiometricProviderStatus.Healthy,
            ModelVersion = Version,
            CheckedAtUtc = DateTime.UtcNow.ToString("O"),
        });
}

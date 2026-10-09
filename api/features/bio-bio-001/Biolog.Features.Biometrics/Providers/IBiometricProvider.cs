using Biolog.Features.Biometrics.Domain;

namespace Biolog.Features.Biometrics.Providers;

/// <summary>
/// BIO-BIO-001-AC01/AC04: the shape every biometric provider adapter shares, regardless of
/// modality - a stable identity plus a health check. <see cref="IFingerprintProvider"/> and
/// <see cref="IFaceProvider"/> extend this with their modality-specific extraction method rather
/// than this interface declaring a generic "extract" method itself, because AC01 asks for
/// "fingerprint AND face provider interfaces" as two distinct contracts a vendor SDK adapter
/// implements one of - not one interface parameterised by modality, which would let a fingerprint
/// adapter be handed a face sample by a caller the type system never stopped.
/// </summary>
public interface IBiometricProvider
{
    /// <summary>Stable identifier for this provider adapter (e.g. "synthetic-fingerprint-v1", a
    /// real vendor's SDK name). Never a per-capture or per-template identifier.</summary>
    string ProviderId { get; }

    BiometricModality Modality { get; }

    Task<BiometricProviderHealth> GetHealthAsync(CancellationToken cancellationToken);
}

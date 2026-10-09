namespace Biolog.Features.Biometrics.Domain;

/// <summary>
/// BIO-BIO-001-AC04: "Provider health and model/version metadata are supported." A snapshot of
/// one provider adapter's health, returned by <see cref="Providers.IBiometricProvider.GetHealthAsync"/>
/// and aggregated by <see cref="Service.BiometricService.GetProviderHealthAsync"/>. Carries the
/// provider's own model/version string (e.g. a vendor SDK build number) so a caller - or an
/// enrolment record, see <see cref="BiometricTemplate.ProviderVersion"/> - can tell which
/// version of a provider produced a given result, without that version string ever being
/// interpreted or validated by this feature (it is opaque, provider-defined).
/// </summary>
public sealed record BiometricProviderHealth
{
    public required string ProviderId { get; init; }
    public required BiometricModality Modality { get; init; }
    public required BiometricProviderStatus Status { get; init; }
    public required string ModelVersion { get; init; }
    public required string CheckedAtUtc { get; init; }

    /// <summary>Present only when <see cref="Status"/> is not <see cref="BiometricProviderStatus.Healthy"/>.</summary>
    public string? Detail { get; init; }
}

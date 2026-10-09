using Biolog.Features.Biometrics.Domain;
using Biolog.Features.Biometrics.Matching;
using Biolog.Features.Biometrics.Providers;

namespace Biolog.Features.Biometrics.Service;

/// <summary>
/// BIO-BIO-001: the provider-neutral orchestration surface identity/enrolment code depends on
/// instead of a vendor SDK directly (the user story's own wording: "vendor SDKs do not leak into
/// core identity logic"). Enrolment and verification both route through
/// <see cref="IFingerprintProvider"/>/<see cref="IFaceProvider"/> for extraction and
/// <see cref="IBiometricTemplateStore"/>/<see cref="IBiometricMatcher"/> for persistence/
/// comparison (AC03) - this class holds no connection string, no SQL, no ORM context of any
/// kind, only those four interfaces. Callers pass a caller-resolved tenant/person id (this
/// project defines no tenant-context seam of its own - see BIO-BIO-001's dependency on
/// BIO-IDN-001, whose <c>PersonService</c> is the caller in the packaged host once a later
/// story wires an HTTP surface onto this service).
/// </summary>
public sealed class BiometricService
{
    private readonly IFingerprintProvider _fingerprintProvider;
    private readonly IFaceProvider _faceProvider;
    private readonly IBiometricTemplateStore _store;
    private readonly IBiometricMatcher _matcher;
    private readonly Func<string> _newId;
    private readonly Func<string> _nowUtc;

    public BiometricService(
        IFingerprintProvider fingerprintProvider,
        IFaceProvider faceProvider,
        IBiometricTemplateStore store,
        IBiometricMatcher matcher,
        Func<string>? newId = null,
        Func<string>? nowUtc = null)
    {
        _fingerprintProvider = fingerprintProvider;
        _faceProvider = faceProvider;
        _store = store;
        _matcher = matcher;
        _newId = newId ?? (() => Guid.NewGuid().ToString());
        _nowUtc = nowUtc ?? (() => DateTime.UtcNow.ToString("O"));
    }

    /// <summary>AC01/AC02: captures a sample through the correct modality's provider, isolates
    /// the result into a canonical <see cref="BiometricTemplate"/>, and persists it via the
    /// store seam. Does not check provider health first - <see cref="ExtractTemplateAsync"/>
    /// itself is the health signal; a provider that cannot serve the request should throw from
    /// extraction rather than this method duplicating a health poll on every call.</summary>
    public async Task<BiometricTemplate> EnrollAsync(
        string tenantId, string personId, BiometricModality modality, BiometricSample sample,
        CancellationToken cancellationToken)
    {
        var features = await ExtractTemplateAsync(modality, sample, cancellationToken).ConfigureAwait(false);
        var providerId = ResolveProvider(modality).ProviderId;
        var template = BiometricTemplate.Create(
            id: _newId(),
            tenantId: tenantId,
            personId: personId,
            modality: modality,
            providerId: providerId,
            providerVersion: features.ProviderVersion,
            payload: features.Payload,
            createdAtUtc: _nowUtc());

        await _store.SaveAsync(template, cancellationToken).ConfigureAwait(false);
        return template;
    }

    /// <summary>AC03: 1:1 verification against the person's most recently enrolled template for
    /// this modality. Throws <see cref="BiometricServiceException"/> (biometric_template_not_found)
    /// when nothing is enrolled - a caller deciding "no biometric on file" is a normal outcome,
    /// not this service silently returning a false match.</summary>
    public async Task<BiometricMatchResult> VerifyAsync(
        string tenantId, string personId, BiometricModality modality, BiometricSample sample,
        CancellationToken cancellationToken)
    {
        var enrolled = await _store.GetTemplatesAsync(tenantId, personId, modality, cancellationToken).ConfigureAwait(false);
        if (enrolled.Count == 0)
        {
            throw BiometricServiceException.NoEnrolledTemplate(personId, modality);
        }

        var features = await ExtractTemplateAsync(modality, sample, cancellationToken).ConfigureAwait(false);
        var providerId = ResolveProvider(modality).ProviderId;
        var probe = BiometricTemplate.Create(
            id: _newId(),
            tenantId: tenantId,
            personId: personId,
            modality: modality,
            providerId: providerId,
            providerVersion: features.ProviderVersion,
            payload: features.Payload,
            createdAtUtc: _nowUtc());

        return await _matcher.CompareAsync(probe, enrolled[0], cancellationToken).ConfigureAwait(false);
    }

    /// <summary>AC04: aggregated health for every registered provider (both modalities).</summary>
    public async Task<IReadOnlyList<BiometricProviderHealth>> GetProviderHealthAsync(CancellationToken cancellationToken)
    {
        var fingerprintHealth = await _fingerprintProvider.GetHealthAsync(cancellationToken).ConfigureAwait(false);
        var faceHealth = await _faceProvider.GetHealthAsync(cancellationToken).ConfigureAwait(false);
        return new[] { fingerprintHealth, faceHealth };
    }

    private async Task<ExtractedBiometricFeatures> ExtractTemplateAsync(
        BiometricModality modality, BiometricSample sample, CancellationToken cancellationToken) => modality switch
    {
        BiometricModality.Fingerprint => await _fingerprintProvider.ExtractTemplateAsync(sample, cancellationToken).ConfigureAwait(false),
        BiometricModality.Face => await _faceProvider.ExtractTemplateAsync(sample, cancellationToken).ConfigureAwait(false),
        _ => throw new ArgumentOutOfRangeException(nameof(modality), modality, "unsupported biometric modality"),
    };

    private IBiometricProvider ResolveProvider(BiometricModality modality) => modality switch
    {
        BiometricModality.Fingerprint => _fingerprintProvider,
        BiometricModality.Face => _faceProvider,
        _ => throw new ArgumentOutOfRangeException(nameof(modality), modality, "unsupported biometric modality"),
    };
}

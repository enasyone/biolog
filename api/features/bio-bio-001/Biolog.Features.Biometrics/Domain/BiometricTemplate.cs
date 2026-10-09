namespace Biolog.Features.Biometrics.Domain;

/// <summary>
/// BIO-BIO-001-AC02: "Provider-specific templates/identifiers are isolated." This is the ONLY
/// biometric template shape that ever crosses out of a provider adapter (see
/// <see cref="Providers.IFingerprintProvider"/>/<see cref="Providers.IFaceProvider"/>) into the
/// rest of BIOLOG (<see cref="Service.BiometricService"/>, <see cref="Matching.IBiometricTemplateStore"/>,
/// <see cref="Matching.IBiometricMatcher"/>). It deliberately carries NO vendor/provider-internal
/// identifier field of any kind - a provider adapter that keeps its own internal reference for a
/// captured sample (a vendor template id, a session handle, a device-side cache key) MUST keep
/// that reference private to itself; it has no field on this type to smuggle it through. Only
/// <see cref="ProviderId"/> (which provider produced this) and <see cref="ProviderVersion"/>
/// (AC04's model/version metadata) name the provider at all, and neither is enough to reach back
/// into that provider's own storage. <see cref="Payload"/> is an opaque byte blob this feature
/// never interprets - only the provider that produced it and a matcher told to compare it know
/// its internal shape.
///
/// <b>Identifiers.</b> <see cref="Id"/>, <see cref="TenantId"/> and <see cref="PersonId"/> are
/// plain, caller-supplied strings, not a validated implementation of ADR-0001's canonical-id
/// contract - the same explicitly documented choice BIO-DOC-001 made for <c>DocumentSession</c>
/// (see that type's remarks): no registered identifier-registry.json prefix exists yet for a
/// biometric template (that registry is Kemi's frozen, ADR-governed contract - see
/// governance/feature-path-ownership.json's read_only_shared_contracts for this story - and
/// amending it is out of this story's scope). A future story that extends the registry with
/// Kemi's approval is the seam this type is written to accept without changing shape.
///
/// <b>Immutability.</b> A template is never edited in place - a person re-enrolling produces a
/// NEW template with a new <see cref="Id"/> (old templates are superseded, not mutated), so this
/// type has no version/CAS machinery: construction only, via <see cref="Create"/>.
///
/// <b>BIO-BIO-001-QA-01 (Seyi round 1, High).</b> <see cref="Payload"/> used to store and return
/// the caller's exact <c>byte[]</c> reference. An independent probe reproduced: enrol a sample,
/// mutate a byte of the returned template's <c>Payload</c>, then read the same template back from
/// <see cref="Synthetic.InMemoryBiometricTemplateStore"/> - the stored candidate had changed,
/// because the returned array and the stored instance's array were the SAME object, so
/// verification could be silently altered without re-enrolment. <see cref="Payload"/> now clones
/// on construction (a caller mutating the array it passed to <see cref="Create"/> cannot reach the
/// template afterwards) and clones again on every read (a caller mutating the array it got back
/// from <see cref="Payload"/> cannot reach the stored instance either) - true immutability at both
/// boundaries, not just one.
/// </summary>
public sealed record BiometricTemplate
{
    private readonly byte[] _payload;

    public string Id { get; }
    public string TenantId { get; }
    public string PersonId { get; }
    public BiometricModality Modality { get; }
    public string ProviderId { get; }
    public string ProviderVersion { get; }
    public byte[] Payload => (byte[])_payload.Clone();
    public string CreatedAtUtc { get; }

    private BiometricTemplate(
        string id, string tenantId, string personId, BiometricModality modality,
        string providerId, string providerVersion, byte[] payload, string createdAtUtc)
    {
        Id = id;
        TenantId = tenantId;
        PersonId = personId;
        Modality = modality;
        ProviderId = providerId;
        ProviderVersion = providerVersion;
        _payload = payload;
        CreatedAtUtc = createdAtUtc;
    }

    public static BiometricTemplate Create(
        string id, string tenantId, string personId, BiometricModality modality,
        string providerId, string providerVersion, byte[] payload, string createdAtUtc)
    {
        RequireNonEmpty(id, nameof(id));
        RequireNonEmpty(tenantId, nameof(tenantId));
        RequireNonEmpty(personId, nameof(personId));
        RequireNonEmpty(providerId, nameof(providerId));
        RequireNonEmpty(providerVersion, nameof(providerVersion));
        RequireNonEmpty(createdAtUtc, nameof(createdAtUtc));
        if (payload is null || payload.Length == 0)
        {
            throw new ArgumentException("biometric_template_payload_empty: a template must carry a non-empty payload.", nameof(payload));
        }

        // Clone on the way in (QA-01): the caller's own array can be mutated afterwards without
        // reaching this template.
        return new BiometricTemplate(id, tenantId, personId, modality, providerId, providerVersion, (byte[])payload.Clone(), createdAtUtc);
    }

    private static void RequireNonEmpty(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("must be a non-empty string", paramName);
        }
    }
}

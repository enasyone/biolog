namespace Biolog.Features.Biometrics.Providers;

/// <summary>
/// BIO-BIO-001-AC02: what a provider adapter is allowed to hand back from extraction - an opaque
/// feature payload plus the provider's own model/version string (AC04). Nothing else. A provider
/// is deliberately context-free: it never sees a tenant or person id and never produces one, so
/// there is no vendor-specific identifier field for this type to carry in the first place - only
/// <see cref="Service.BiometricService"/> (which DOES know the tenant/person making the request)
/// turns this into a tenant/person-scoped <see cref="Domain.BiometricTemplate"/>. A provider that
/// keeps its own internal bookkeeping for a capture (a vendor template id, a session handle) is
/// free to do so entirely in private fields of its own implementation class - there is no field
/// here it could use to leak that reference out even if it wanted to.
///
/// <b>BIO-BIO-001-QA-01 (Seyi round 1, High).</b> Same aliasing risk <see cref="Domain.BiometricTemplate"/>
/// had: a provider handing back its own working buffer (or a caller mutating what they read)
/// could silently change the bytes a <see cref="Service.BiometricService"/> enrolment later
/// persists. <see cref="Payload"/> clones on both <c>init</c> and read, so neither side can reach
/// the other's array through this type.
/// </summary>
public sealed record ExtractedBiometricFeatures
{
    private readonly byte[] _payload = Array.Empty<byte>();

    public required byte[] Payload
    {
        get => (byte[])_payload.Clone();
        init => _payload = (byte[])value.Clone();
    }

    public required string ProviderVersion { get; init; }
}

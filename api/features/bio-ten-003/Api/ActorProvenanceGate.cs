using System.Security.Cryptography;
using System.Text;
using Biolog.Features.TenantFeatureConfig.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace Biolog.Features.TenantFeatureConfig.Api;

/// <summary>
/// Round-2 remediation (ZB-TEN003-01/BIO-TEN-003-F01, Critical). ADR-0006 Decision 3, applied
/// the same way BIO-MOB-005/BIO-ACC-001/BIO-INT-002 already apply it: this feature's
/// administrative read/write endpoints gate on this because they ARE an authorization decision
/// (which caller may read or change a tenant's feature posture), not merely audit attribution -
/// the asserted X-Actor-Id header alone is unsafe as the sole input to that decision per
/// ADR-0006 Decision 2.
///
/// Project-local copy of the identical mechanism, not a weaker one, per this repository's
/// "each feature owns its own small primitives" convention.
/// </summary>
public interface IActorProvenanceStore
{
    void Configure(string sharedSecret);

    bool Verify(string actorId, string presentedProofHex);
}

public sealed class HmacActorProvenanceStore : IActorProvenanceStore
{
    /// <summary>
    /// Round-3 remediation (BIO-TEN-003-F05, Critical, Seyi + Zainab independently; the same
    /// shape already proven across 14 files in TD-PROVENANCE-DUMMYKEY-SWEEP - ported here, not
    /// re-derived). Round 2 fixed the UNCONFIGURED case (a random, unknowable fallback key) but
    /// left the CONFIGURED case unguarded: a short or typo'd real secret is a genuinely
    /// brute-forceable HMAC key, not a dummy-key problem. 32 bytes is the canonical floor this
    /// sweep established for every provenance/token secret in this codebase.
    /// </summary>
    public const int MinimumSecretLength = 32;

    private byte[]? _key;

    /// <summary>Round-3 remediation (BIO-TEN-003-F05): a secret shorter than <see cref="MinimumSecretLength"/> is rejected - <c>_key</c> stays <c>null</c>, the same state as never having been configured at all, so <see cref="Verify"/>'s single fail-closed branch covers both cases uniformly.</summary>
    public void Configure(string sharedSecret)
    {
        var candidate = Encoding.UTF8.GetBytes(sharedSecret);
        _key = candidate.Length >= MinimumSecretLength ? candidate : null;
    }

    /// <summary>Round-3 remediation (BIO-TEN-003-F05): no fallback key of any kind. An unconfigured OR too-short secret means every proof fails verification - fail CLOSED, never forgeable, never brute-forceable via a short real key.</summary>
    public bool Verify(string actorId, string presentedProofHex)
    {
        if (_key is null)
        {
            return false;
        }

        var expected = HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(actorId));

        byte[] presented;
        try
        {
            presented = Convert.FromHexString(presentedProofHex);
        }
        catch (FormatException)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(presented, expected);
    }
}

/// <summary>Reads <c>TenantFeatureConfig:ActorProvenanceSecret</c> - this feature's own configuration key. No secret value is ever committed to source.</summary>
public static class ActorProvenanceProvisioning
{
    public const string ConfigurationKey = "TenantFeatureConfig:ActorProvenanceSecret";

    public static IActorProvenanceStore LoadFromConfiguration(IConfiguration configuration)
    {
        var store = new HmacActorProvenanceStore();
        var value = configuration[ConfigurationKey];
        if (!string.IsNullOrWhiteSpace(value))
        {
            store.Configure(value);
        }

        return store;
    }
}

public sealed class ActorProvenanceGate(IActorProvenanceStore store)
{
    public const string ProvenanceHeader = "X-Actor-Provenance";

    public void Require(HttpRequest request, string actorId)
    {
        var presentedProof = request.Headers[ProvenanceHeader].ToString();
        if (string.IsNullOrEmpty(presentedProof) || !store.Verify(actorId, presentedProof))
        {
            throw TenantFeatureConfigurationError.ActorProvenanceRequired();
        }
    }
}

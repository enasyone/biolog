using System.Security.Cryptography;
using System.Text;
using Biolog.Features.AccessCache.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace Biolog.Features.AccessCache.Api;

/// <summary>
/// ADR-0006 Decision 3, applied here the same way BIO-ACC-001 already applies it: the
/// application-side half of "X-Actor-Id may remain the transport, but only if ... the
/// application checks that injected value's provenance before trusting it for an authorization
/// decision." This feature's one human-facing endpoint (the browser-side <c>AccessService</c>
/// "view access events" read) gates on this the same way BIO-ACC-001 gates its own read
/// endpoints - reading presentation events is itself an authorization decision (which tenant's
/// data the caller may see), not merely audit attribution.
///
/// See <c>api/features/bio-acc-001/Api/ActorProvenanceGate.cs</c> for the full ADR-0006
/// rationale and disclosed residual scope (not authentication, no replay protection, no
/// separation-of-duties) - this is a project-local copy of the identical mechanism, not a
/// weaker one, per this repository's "each feature owns its own small primitives" convention.
/// </summary>
public interface IActorProvenanceStore
{
    void Configure(string sharedSecret);

    bool Verify(string actorId, string presentedProofHex);
}

public sealed class HmacActorProvenanceStore : IActorProvenanceStore
{
    public const int MinimumSecretLength = 32;

    private byte[]? _key;

    /// <summary>TD-PROVENANCE-DUMMYKEY-SWEEP, normalization pass (Kemi's ruling): a secret shorter than <see cref="MinimumSecretLength"/> is rejected outright - an operator typo'ing a short/weak secret must not be silently accepted and used as a real, brute-forceable HMAC key. No fallback key of any kind: <see cref="Verify"/> returns false whenever <c>_key</c> is null, never computed against a dummy.</summary>
    public void Configure(string sharedSecret) =>
        _key = sharedSecret.Length < MinimumSecretLength ? null : Encoding.UTF8.GetBytes(sharedSecret);

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

/// <summary>Reads <c>AccessCache:ActorProvenanceSecret</c> - this feature's own configuration key, distinct from BIO-ACC-001's <c>AccessPolicy:ActorProvenanceSecret</c>. No secret value is ever committed to source.</summary>
public static class ActorProvenanceProvisioning
{
    public const string ConfigurationKey = "AccessCache:ActorProvenanceSecret";

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
            throw AccessCacheError.ActorProvenanceRequired();
        }
    }
}

using System.Security.Cryptography;
using System.Text;
using Biolog.Features.MobileVerification.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace Biolog.Features.MobileVerification.Api;

/// <summary>
/// ADR-0006 Decision 3, applied here the same way BIO-ACC-001/BIO-ACC-003/BIO-MOB-005 already
/// apply it: this feature's one human-facing device-role write endpoint gates on this the same
/// way those stories gate their own writes - it is itself an authorization-relevant action, not
/// merely audit attribution.
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

/// <summary>Reads <c>MobileVerification:ActorProvenanceSecret</c> - this feature's own configuration key. No secret value is ever committed to source.</summary>
public static class ActorProvenanceProvisioning
{
    public const string ConfigurationKey = "MobileVerification:ActorProvenanceSecret";

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
            throw VerificationError.ActorProvenanceRequired();
        }
    }
}

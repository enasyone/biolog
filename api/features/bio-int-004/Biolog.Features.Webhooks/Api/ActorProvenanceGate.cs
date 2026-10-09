using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace Biolog.Features.Webhooks.Api;

/// <summary>Thrown when <c>X-Actor-Provenance</c> is missing or does not verify against the
/// already-resolved actor id. Maps to HTTP 401 in <c>BioInt004Module</c> - deliberately
/// indistinguishable from a missing proof, same as every sibling story's own gate.</summary>
public sealed class ActorProvenanceRequiredException() : Exception(
    "this action requires a valid X-Actor-Provenance proof for the asserted X-Actor-Id (ADR-0006 Decision 3)");

/// <summary>
/// ADR-0006 Decision 3 - the application-side half of the interim mitigation: verifies the
/// actor id <see cref="ActorResolver"/> already resolved was genuinely accompanied by a
/// provenance proof (an HMAC-SHA256 of that exact actor id, keyed by a shared secret).
/// Fail-closed from this story's own first version, not retrofitted after a review finding
/// (TD-PROVENANCE-DUMMYKEY-SWEEP) - <see cref="HmacActorProvenanceStore.Verify"/> always
/// returns <c>false</c> when unconfigured or below the minimum length, no fallback key.
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

    public void Configure(string sharedSecret) =>
        _key = sharedSecret.Length < MinimumSecretLength ? null : Encoding.UTF8.GetBytes(sharedSecret);

    public bool Verify(string actorId, string presentedProofHex)
    {
        if (_key is null)
        {
            return false;
        }

        byte[] presented;
        try
        {
            presented = Convert.FromHexString(presentedProofHex);
        }
        catch (FormatException)
        {
            return false;
        }

        var expected = HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(actorId));
        return CryptographicOperations.FixedTimeEquals(presented, expected);
    }
}

/// <summary>Reads <c>Webhooks:ActorProvenanceSecret</c> from <see cref="IConfiguration"/> -
/// the same out-of-band provisioning path every sibling story's own provenance store uses. No
/// secret value is ever committed to source.</summary>
public static class ActorProvenanceProvisioning
{
    public const string ConfigurationKey = "Webhooks:ActorProvenanceSecret";

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
            throw new ActorProvenanceRequiredException();
        }
    }
}

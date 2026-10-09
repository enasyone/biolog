using System.Security.Cryptography;
using System.Text;
using Biolog.Features.AccessDecisions.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace Biolog.Features.AccessDecisions.Api;

/// <summary>
/// ADR-0006 Decision 3, hand-ported feature-locally exactly as BIO-ACC-001's/BIO-ATT-001's/
/// BIO-VIS-001's own <c>ActorProvenanceGate.cs</c> already established (not repeated here). Used
/// by <see cref="AccessDecisionEndpoints"/>'s own decision route - the request that reaches
/// BIO-ACC-001's own gated <c>PolicyEvaluationService.EvaluateAsync</c> through
/// <c>IPolicyDecisionGateway</c>. Unlike BIO-TRN-002/BIO-ATT-001's own capture endpoints, THIS
/// feature's decision route genuinely produces an authorization outcome (ADR-0006 Decision 1/2),
/// so it carries the identical provenance requirement BIO-ACC-001's own <c>/evaluate</c> endpoint
/// enforces for the same underlying action - skipping it here would let an unprovenanced caller
/// reach the same decision through a side door.
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

/// <summary>Reads <c>AccessDecisions:ActorProvenanceSecret</c> from <see cref="IConfiguration"/> - the same out-of-band provisioning path every sibling story's own provenance store uses. No secret value is ever committed to source.</summary>
public static class ActorProvenanceProvisioning
{
    public const string ConfigurationKey = "AccessDecisions:ActorProvenanceSecret";

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
            throw AccessDecisionError.ActorProvenanceRequired();
        }
    }
}

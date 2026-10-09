using System.Security.Cryptography;
using System.Text;
using Biolog.Features.BiometricRetention.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace Biolog.Features.BiometricRetention.Api;

/// <summary>
/// ADR-0006 Decision 3, applied the same way BIO-ACC-001/BIO-ACC-003/BIO-MOB-005 already apply
/// it: retention-policy writes and template deletion are each an authorization-relevant action
/// (not merely audit attribution), so the asserted <c>X-Actor-Id</c> alone is not a safe
/// authorization basis (Decision 2) - it must be bound to a verified provenance proof before any
/// permission lookup is trusted.
///
/// Project-local copy of the identical mechanism, not a weaker one, per this repository's "each
/// feature owns its own small primitives" convention - deliberately NOT a reference to
/// BIO-MOB-005's own instance.
/// </summary>
public interface IActorProvenanceStore
{
    void Configure(string sharedSecret);

    bool Verify(string actorId, string presentedProofHex);
}

public sealed class HmacActorProvenanceStore : IActorProvenanceStore
{
    /// <summary>Round 4 (god's dispatch, now Kemi-ruled canonical floor-wide, matching
    /// BIO-INT-003's own <c>ActorProvenanceGate.cs</c> exactly): a secret shorter than this is
    /// treated the same as unconfigured - too easy to guess/brute-force to anchor a real HMAC
    /// key.</summary>
    public const int MinimumSecretLength = 32;

    private byte[]? _key;

    public void Configure(string sharedSecret) =>
        _key = sharedSecret.Length < MinimumSecretLength ? null : Encoding.UTF8.GetBytes(sharedSecret);

    public bool Verify(string actorId, string presentedProofHex)
    {
        // ZB-BIO009-05: no public, derivable fallback key. An unconfigured (or too-short, round
        // 4) secret must fail every verification, never fall back to a key an attacker can
        // compute from source or brute-force.
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

/// <summary>Reads <c>BiometricRetention:ActorProvenanceSecret</c> - this feature's own
/// configuration key. No secret value is ever committed to source.</summary>
public static class ActorProvenanceProvisioning
{
    public const string ConfigurationKey = "BiometricRetention:ActorProvenanceSecret";

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
            throw BiometricRetentionException.ActorProvenanceRequired();
        }
    }
}

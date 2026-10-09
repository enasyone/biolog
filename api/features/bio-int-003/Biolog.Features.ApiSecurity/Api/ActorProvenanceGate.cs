using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace Biolog.Features.ApiSecurity.Api;

/// <summary>Thrown when <c>X-Actor-Provenance</c> is missing or does not verify against the
/// already-resolved actor id. Maps to HTTP 401 in <c>BioInt003Module</c> - deliberately
/// indistinguishable from a missing proof, same as every sibling story's own gate.</summary>
public sealed class ActorProvenanceRequiredException() : Exception(
    "this action requires a valid X-Actor-Provenance proof for the asserted X-Actor-Id (ADR-0006 Decision 3)");

/// <summary>
/// ADR-0006 Decision 3 (<c>docs/adr/ADR-0006-actor-identity-and-authorization-boundary.md</c>) -
/// the application-side half of the interim mitigation: verifies the actor id
/// <see cref="ActorResolver"/> already resolved was genuinely accompanied by a provenance proof
/// (an HMAC-SHA256 of that exact actor id, keyed by a shared secret) - not an independent flag
/// that waves any request through regardless of which actor id it names.
///
/// <b>Security-review fix (Zainab, ZB-INT003-05, Critical).</b> The byte-for-byte copy of
/// BIO-ACC-001's/BIO-IDN-006's own <c>HmacActorProvenanceStore</c> carried THEIR identical bug
/// forward: its "dummy key" was <c>SHA256("no-provenance-secret-configured")</c> - a literal
/// string checked into source, so anyone reading it could compute
/// <c>HMAC-SHA256(dummyKey, anyPrivilegedActorId)</c>, pass this gate while asserting that
/// actor's id, and have <see cref="ServiceAccountService"/> query THAT actor's real permission
/// grants - recreating exactly the impersonation bypass this gate exists to close. Confirmed
/// copy-pasted across ~12 other already-merged stories and escalated floor-wide separately; this
/// file's own fix is the same fail-closed discipline <c>HmacApiAccessTokenCodec</c>/
/// <c>HmacApiSecretHasher</c> already apply for ZB-INT003-01 - NO fallback key of any kind.
/// <see cref="HmacActorProvenanceStore.Verify"/> always returns <c>false</c> when unconfigured
/// (or configured below <see cref="HmacActorProvenanceStore.MinimumSecretLength"/>), full stop -
/// there is no key, dummy or otherwise, for any presented proof to match.
///
/// The EDGE half - the trusted component that strips a caller-supplied <c>X-Actor-Id</c>,
/// verifies the real caller, and injects a matching (verified actor id, provenance proof) pair -
/// is Emeka's own infrastructure concern (BIO-INT-001/deployment boundary), outside this story's
/// surface. Until that edge exists, an operator/deployment script computes the same HMAC out of
/// band and attaches it to legitimate requests.
/// </summary>
public interface IActorProvenanceStore
{
    /// <summary>Out-of-band provisioning only (see <see cref="ActorProvenanceProvisioning"/>). Not reachable from any HTTP endpoint.</summary>
    void Configure(string sharedSecret);

    /// <summary>Constant-time. Fails CLOSED (always false) when no secret was ever configured
    /// (or it is shorter than <see cref="HmacActorProvenanceStore.MinimumSecretLength"/>), or when
    /// the presented value is not valid hex - never computed against any fallback key.</summary>
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

/// <summary>Reads <c>ApiSecurity:ActorProvenanceSecret</c> from <see cref="IConfiguration"/> -
/// the same out-of-band provisioning path BIO-ACC-001's/BIO-IDN-006's own
/// <c>ActorProvenanceProvisioning</c> already established. No secret value is ever committed to
/// source.</summary>
public static class ActorProvenanceProvisioning
{
    public const string ConfigurationKey = "ApiSecurity:ActorProvenanceSecret";

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

/// <summary>The actual per-request gate - see this file's own header for the full ADR-0006
/// Decision 3 rationale.</summary>
public sealed class ActorProvenanceGate(IActorProvenanceStore store)
{
    public const string ProvenanceHeader = "X-Actor-Provenance";

    /// <summary>Called AFTER <see cref="ActorResolver.Resolve"/> - <paramref name="actorId"/> is
    /// the already-resolved actor id, never re-parsed from the header directly.</summary>
    public void Require(HttpRequest request, string actorId)
    {
        var presentedProof = request.Headers[ProvenanceHeader].ToString();
        if (string.IsNullOrEmpty(presentedProof) || !store.Verify(actorId, presentedProof))
        {
            throw new ActorProvenanceRequiredException();
        }
    }
}

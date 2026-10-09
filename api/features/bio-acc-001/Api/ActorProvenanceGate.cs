using System.Security.Cryptography;
using System.Text;
using Biolog.Features.AccessPolicy.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace Biolog.Features.AccessPolicy.Api;

/// <summary>
/// ADR-0006 Decision 3 (<c>docs/adr/ADR-0006-actor-identity-and-authorization-boundary.md</c>) -
/// the authoritative text this class implements against, applied from the start rather than
/// discovered through review (BIO-IDN-006-QA-08's own history). Decision 3's own words:
/// "X-Actor-Id may remain the transport, but only if a trusted edge strips any caller-supplied
/// value and injects a verified one, and the application checks that injected value's provenance
/// before trusting it for an authorization decision - never accepting a caller-supplied value
/// directly." This class is the APPLICATION-SIDE half of that: it verifies the specific actor id
/// <see cref="ActorResolver"/> already resolved was genuinely accompanied by a provenance proof -
/// an HMAC-SHA256 of THAT EXACT actor id, keyed by a shared provenance secret - that only
/// something holding the secret could have produced FOR THAT VALUE. This is the real difference
/// from a bare shared secret: the proof is cryptographically bound to the specific identity being
/// asserted in this request, not an independent flag that waves any request through regardless of
/// which actor id it names.
///
/// This story's own mutating endpoints (policy authoring/publishing) AND its evaluation endpoint
/// both use this gate: authoring gates on the calling actor's own authority to define policy, and
/// evaluation gates on the calling actor's own authority to query it - both are authorization
/// decisions per ADR-0006 Decision 1/2, not merely audit attribution.
///
/// The EDGE half - the trusted component that actually strips a caller-supplied
/// <c>X-Actor-Id</c>, verifies who the caller really is by whatever mechanism, and injects the
/// matching (verified actor id, provenance proof) pair - is explicitly Emeka's infrastructure
/// concern (BIO-INT-001/deployment boundary), outside this story's own surface. Until that edge
/// exists, an operator/deployment script computes the SAME HMAC out of band and attaches it to
/// legitimate requests - modelling exactly what the edge will eventually automate.
///
/// <b>What this explicitly does NOT buy</b> (ADR-0006 Decision 3's own "what this interim
/// closure explicitly does not buy" - stated here so it is never overclaimed):
/// - NOT authentication. It does not issue, verify or expire a credential for a specific person.
/// - NOT session revocation or replay protection beyond whatever the trusted edge itself
///   provides.
/// - Does NOT restore separation-of-duties by itself: whoever holds the shared provenance secret
///   can compute a valid proof for ANY actor id, including two different ids across two calls -
///   provenance proves "this passed through something holding the shared secret," never "this is
///   a specific, distinct, verified person." This residual is accepted and disclosed per
///   Decision 3, not something this story's own permission/maker-checker checks are expected to
///   close on their own.
/// </summary>
public interface IActorProvenanceStore
{
    /// <summary>Out-of-band provisioning only (see <see cref="ActorProvenanceProvisioning"/>). Not reachable from any HTTP endpoint.</summary>
    void Configure(string sharedSecret);

    /// <summary>
    /// Constant-time. Verifies <paramref name="presentedProofHex"/> is the lowercase-hex-encoded
    /// HMAC-SHA256 of <paramref name="actorId"/> under the configured shared secret - the proof
    /// is bound to THIS specific actor id, not an independent token. Fails CLOSED (always false)
    /// when no secret was ever configured, or when the presented value is not valid hex.
    /// </summary>
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
            return false; // malformed hex can never equal a real 32-byte HMAC
        }

        return CryptographicOperations.FixedTimeEquals(presented, expected);
    }
}

/// <summary>
/// Reads <c>AccessPolicy:ActorProvenanceSecret</c> from <see cref="IConfiguration"/> - the same
/// out-of-band provisioning path <c>TenantCredentialProvisioning</c>/BIO-IDN-006's own
/// <c>ActorProvenanceProvisioning</c> already established. No secret value is ever committed to
/// source: nothing under this feature's checked-in <c>appsettings*.json</c> declares this key.
/// Deliberately not an HTTP endpoint - provisioning is an operator/deployment concern, and until
/// Emeka's real trusted edge exists, this same secret is what a deployment/operator script uses
/// to compute the provenance proof it attaches to legitimate requests.
/// </summary>
public static class ActorProvenanceProvisioning
{
    public const string ConfigurationKey = "AccessPolicy:ActorProvenanceSecret";

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

/// <summary>The actual per-request gate - see this file's own header for the full ADR-0006 Decision 3 rationale.</summary>
public sealed class ActorProvenanceGate(IActorProvenanceStore store)
{
    public const string ProvenanceHeader = "X-Actor-Provenance";

    /// <summary>Called AFTER <see cref="ActorResolver.Resolve"/> - <paramref name="actorId"/> is the already-resolved actor id, never re-parsed from the header directly, so this verifies provenance of the SAME value any subsequent authorization decision will use.</summary>
    public void Require(HttpRequest request, string actorId)
    {
        var presentedProof = request.Headers[ProvenanceHeader].ToString();
        if (string.IsNullOrEmpty(presentedProof) || !store.Verify(actorId, presentedProof))
        {
            throw AccessPolicyError.ActorProvenanceRequired();
        }
    }
}

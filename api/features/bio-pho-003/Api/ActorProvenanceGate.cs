using System.Security.Cryptography;
using System.Text;
using Biolog.Features.PhotoQuality.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace Biolog.Features.PhotoQuality.Api;

/// <summary>
/// ADR-0006 Decision 3 - the application-side half of the interim mitigation: verifies the
/// actor id <see cref="ActorResolver"/> already resolved was genuinely accompanied by a
/// provenance proof (an HMAC-SHA256 of that exact actor id, keyed by a shared secret) - not an
/// independent flag that waves any request through regardless of which actor id it names.
///
/// ZB-PHO003-01 (Zainab, security round 1, Critical): round 1 of this story had no provenance or
/// permission gate on any of its five routes at all - any caller could assert an arbitrary
/// <c>X-Actor-Id</c> and submit/approve/reject a capture, or read captures for any person, with
/// no proof of identity and no authorization check. <see cref="ActorResolver"/>'s own former
/// header ("no authentication story has landed yet... crosses no privilege-escalation boundary")
/// was stale by the time this story's own write path reached <c>PhotoMasterService.AppendAsync</c>
/// - this build already contains the canonical provenance+RBAC infrastructure every current
/// tenant-plane story uses, and approving a capture here genuinely does replace a person's
/// authoritative photo.
///
/// Hand-ported feature-locally from the CURRENT canonical shape - verified directly against
/// <c>api/features/bio-int-003/Biolog.Features.ApiSecurity/Api/ActorProvenanceGate.cs</c> (and its
/// most recent sibling copy, <c>api/features/bio-int-004/.../Api/ActorProvenanceGate.cs</c>)
/// before porting: no fallback/dummy key of any kind - <see cref="HmacActorProvenanceStore.Verify"/>
/// always returns <c>false</c> when unconfigured (or configured below
/// <see cref="HmacActorProvenanceStore.MinimumSecretLength"/>), full stop.
///
/// The EDGE half - the trusted component that strips a caller-supplied <c>X-Actor-Id</c>, verifies
/// the real caller, and injects a matching (verified actor id, provenance proof) pair - is
/// <c>web/proxy.ts</c> (TD-WEB-PROVENANCE-BRIDGE), outside this story's surface, the same
/// disclosed boundary every sibling story's own gate carries. <c>web/lib/actor-provenance-routes.ts</c>
/// carries this feature's own <c>PhotoQuality:ActorProvenanceSecret</c> entry so the bridge
/// actually injects a proof for <c>/api/v1/photo-quality/*</c>.
/// </summary>
public interface IActorProvenanceStore
{
    /// <summary>Out-of-band provisioning only. Not reachable from any HTTP endpoint.</summary>
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

/// <summary>Reads <c>PhotoQuality:ActorProvenanceSecret</c> from <see cref="IConfiguration"/> - the same out-of-band provisioning path every sibling story's own provenance store uses. No secret value is ever committed to source.</summary>
public static class ActorProvenanceProvisioning
{
    public const string ConfigurationKey = "PhotoQuality:ActorProvenanceSecret";

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

/// <summary>The actual per-request gate - see this file's own header for the full ADR-0006 Decision 3 / ZB-PHO003-01 rationale.</summary>
public sealed class ActorProvenanceGate(IActorProvenanceStore store)
{
    public const string ProvenanceHeader = "X-Actor-Provenance";

    /// <summary>Called AFTER <see cref="ActorResolver.Resolve"/> - <paramref name="actorId"/> is the already-resolved actor id, never re-parsed from the header directly.</summary>
    public void Require(HttpRequest request, string actorId)
    {
        var presentedProof = request.Headers[ProvenanceHeader].ToString();
        if (string.IsNullOrEmpty(presentedProof) || !store.Verify(actorId, presentedProof))
        {
            throw PhotoQualityError.ActorProvenanceRequired();
        }
    }
}

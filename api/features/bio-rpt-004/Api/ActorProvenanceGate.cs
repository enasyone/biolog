using System.Security.Cryptography;
using System.Text;
using Biolog.Features.OperationalDashboard.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace Biolog.Features.OperationalDashboard.Api;

/// <summary>
/// ADR-0006 Decision 3 (<c>docs/adr/ADR-0006-actor-identity-and-authorization-boundary.md</c>) -
/// the application-side half of the interim mitigation: verifies the actor id
/// <see cref="ActorIdHeader"/> already resolved was genuinely accompanied by a provenance proof
/// (an HMAC-SHA256 of that exact actor id, keyed by a shared secret) - not an independent flag
/// that waves any request through regardless of which actor id it names.
///
/// Seyi round-2, BIO-RPT-004-F03 (Critical): round 2's real `IDashboardPermissionGateway` check
/// trusted a raw, caller-asserted `X-Actor-Id` directly - a caller who knew or guessed a
/// privileged PERSON id could assert it and inherit that person's real `dashboard.view_operational_health`
/// grant, with no proof the request actually came from them. This gate closes that: it runs
/// BEFORE the permission check and fails the request outright if no valid provenance proof
/// accompanies the asserted actor id.
///
/// Hand-ported feature-locally from the CURRENT canonical shape (god's explicit instruction: build
/// on the pattern Zainab's BIO-INT-003 round-2 fix established - commit c5e2f9d,
/// `api/features/bio-int-003/Biolog.Features.ApiSecurity/Api/ActorProvenanceGate.cs` - not the
/// earlier, superseded dummy-key version several other sibling stories still carry). Verified
/// directly against that file before porting: no fallback/dummy key of any kind -
/// <see cref="HmacActorProvenanceStore.Verify"/> always returns <c>false</c> when unconfigured (or
/// configured below <see cref="HmacActorProvenanceStore.MinimumSecretLength"/>), full stop.
///
/// The EDGE half - the trusted component that strips a caller-supplied <c>X-Actor-Id</c>, verifies
/// the real caller, and injects a matching (verified actor id, provenance proof) pair - is
/// Emeka's own infrastructure concern (BIO-INT-001/deployment boundary), outside this story's
/// surface, the same disclosed boundary every sibling story's own gate carries. Until that edge
/// exists, no frontend live port in this codebase sends a real `X-Actor-Provenance` value
/// (verified directly - none do), so this feature's own LIVE mode honestly always returns 401
/// `actor_provenance_required` for the demo session, never silently bypassing the gate.
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

/// <summary>Reads <c>OperationalDashboard:ActorProvenanceSecret</c> from <see cref="IConfiguration"/> - the same out-of-band provisioning path every sibling story's own provenance store uses. No secret value is ever committed to source.</summary>
public static class ActorProvenanceProvisioning
{
    public const string ConfigurationKey = "OperationalDashboard:ActorProvenanceSecret";

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

    /// <summary>Called AFTER <see cref="ActorIdHeader.Resolve"/> - <paramref name="actorId"/> is the already-resolved actor id, never re-parsed from the header directly.</summary>
    public void Require(HttpRequest request, string actorId)
    {
        var presentedProof = request.Headers[ProvenanceHeader].ToString();
        if (string.IsNullOrEmpty(presentedProof) || !store.Verify(actorId, presentedProof))
        {
            throw DashboardError.ActorProvenanceRequired();
        }
    }
}

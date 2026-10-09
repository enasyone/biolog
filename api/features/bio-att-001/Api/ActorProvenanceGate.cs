using System.Security.Cryptography;
using System.Text;
using Biolog.Features.Attendance.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace Biolog.Features.Attendance.Api;

/// <summary>
/// ADR-0006 Decision 3 (<c>docs/adr/ADR-0006-actor-identity-and-authorization-boundary.md</c>),
/// hand-ported feature-locally exactly as BIO-ACC-001's own <c>ActorProvenanceGate.cs</c> already
/// established (that class's own header carries the full ADR-0006 rationale verbatim; not
/// repeated here). Used ONLY by <c>AttendancePolicyService.SetAsync</c> - this feature's one
/// authorization decision (ADR-0006 Decision 1/2) - never by capture or read, which stay
/// audit-attribution-only per Decision 2 (see <see cref="ActorResolver"/>'s header).
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

/// <summary>Reads <c>Attendance:ActorProvenanceSecret</c> from <see cref="IConfiguration"/> - the same out-of-band provisioning path every sibling story's own provenance store uses. No secret value is ever committed to source.</summary>
public static class ActorProvenanceProvisioning
{
    public const string ConfigurationKey = "Attendance:ActorProvenanceSecret";

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
            throw AttendanceError.ActorProvenanceRequired();
        }
    }
}

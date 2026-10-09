using System.Security.Cryptography;
using System.Text;
using Biolog.Features.ImportConflictReview.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace Biolog.Features.ImportConflictReview.Api;

/// <summary>
/// ADR-0006 Decision 3, hand-ported feature-locally exactly as every sibling story's own
/// <c>ActorProvenanceGate.cs</c> already established (not repeated here, already includes
/// TD-PROVENANCE-DUMMYKEY-SWEEP's minimum-length floor from the start). Used ONLY by
/// <c>ImportConflictReviewService.DecideAsync</c> - this feature's one authorization decision
/// (ADR-0006 Decision 1/2) - never by scan/list, which stay audit-attribution-only per
/// Decision 2.
///
/// <b>Deliberately reuses BIO-INT-002's own <c>Integrations:ActorProvenanceSecret</c>
/// configuration key, not a new one.</b> This feature's endpoints are nested under the SAME
/// <c>/api/v1/integrations/mappings/{mappingId}/conflicts/*</c> path prefix BIO-INT-002's own
/// mapping routes already use (see <c>ImportConflictReviewEndpoints</c>'s header) - a prefix
/// already registered in <c>web/lib/actor-provenance-routes.ts</c> as feature <c>Integrations</c>.
/// Reusing the same configuration key means that registration, and whatever secret an operator
/// already configured for it, covers these new routes automatically with no route-table or web
/// bridge change needed - two independent module instances (BIO-INT-002's and this story's own)
/// simply read the same configured value from <see cref="IConfiguration"/>, never shared code.
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

    /// <summary>TD-PROVENANCE-DUMMYKEY-SWEEP shape (Kemi's canonical ruling): a secret shorter than <see cref="MinimumSecretLength"/> is rejected outright - an operator typo'ing a short/weak secret must not be silently accepted and used as a real, brute-forceable HMAC key. No fallback key of any kind: <see cref="Verify"/> returns false whenever <c>_key</c> is null, never computed against a dummy.</summary>
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

/// <summary>Reads <c>Integrations:ActorProvenanceSecret</c> from <see cref="IConfiguration"/> - the SAME key BIO-INT-002's own provenance store reads (see this file's own header for why). No secret value is ever committed to source.</summary>
public static class ActorProvenanceProvisioning
{
    public const string ConfigurationKey = "Integrations:ActorProvenanceSecret";

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
            throw ImportConflictReviewError.ActorProvenanceRequired();
        }
    }
}

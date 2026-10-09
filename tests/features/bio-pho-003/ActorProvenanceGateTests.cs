// Developer tests for BIO-PHO-003 (ZB-PHO003-01). SYNTHETIC: the secret below is a literal test
// fixture, never a real deployment value.
using System.Security.Cryptography;
using System.Text;
using Biolog.Features.PhotoQuality.Api;
using Xunit;

namespace Biolog.Features.PhotoQuality.Tests;

/// <summary>TD-PROVENANCE-DUMMYKEY-SWEEP's own normalization, applied here from the start - the configured secret itself must meet a canonical minimum length, with no fallback key of any kind.</summary>
public sealed class ActorProvenanceGateTests
{
    private static string SecretOfLength(int length) => new string('k', length);

    private static string Proof(string secret, string actorId) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(actorId)));

    [Fact]
    public void A_secret_one_byte_below_the_minimum_is_rejected_every_proof_fails()
    {
        var store = new HmacActorProvenanceStore();
        var secret = SecretOfLength(HmacActorProvenanceStore.MinimumSecretLength - 1);
        store.Configure(secret);

        Assert.False(store.Verify("per_actor", Proof(secret, "per_actor")));
    }

    [Fact]
    public void A_secret_exactly_at_the_minimum_is_accepted_a_correct_proof_verifies()
    {
        var store = new HmacActorProvenanceStore();
        var secret = SecretOfLength(HmacActorProvenanceStore.MinimumSecretLength);
        store.Configure(secret);

        Assert.True(store.Verify("per_actor", Proof(secret, "per_actor")));
    }

    [Fact]
    public void A_never_configured_store_rejects_every_proof()
    {
        var store = new HmacActorProvenanceStore();

        Assert.False(store.Verify("per_actor", Proof(SecretOfLength(64), "per_actor")));
    }

    [Fact]
    public void A_proof_computed_under_a_different_secret_never_verifies()
    {
        var store = new HmacActorProvenanceStore();
        store.Configure(SecretOfLength(HmacActorProvenanceStore.MinimumSecretLength));

        var wrongProof = Proof(SecretOfLength(HmacActorProvenanceStore.MinimumSecretLength) + "x", "per_actor");

        Assert.False(store.Verify("per_actor", wrongProof));
    }

    [Fact]
    public void Malformed_hex_never_verifies()
    {
        var store = new HmacActorProvenanceStore();
        store.Configure(SecretOfLength(HmacActorProvenanceStore.MinimumSecretLength));

        Assert.False(store.Verify("per_actor", "not-valid-hex"));
    }

    [Fact]
    public void A_proof_for_a_different_actor_id_under_the_same_secret_never_verifies()
    {
        var store = new HmacActorProvenanceStore();
        var secret = SecretOfLength(HmacActorProvenanceStore.MinimumSecretLength);
        store.Configure(secret);

        var proofForSomeoneElse = Proof(secret, "per_someone_else");

        Assert.False(store.Verify("per_actor", proofForSomeoneElse));
    }
}

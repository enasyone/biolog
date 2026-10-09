using System.Security.Cryptography;
using System.Text;
using Biolog.Features.TenantFeatureConfig.Api;
using Xunit;

namespace Biolog.Features.TenantFeatureConfig.Tests;

/// <summary>
/// Round-3 remediation (BIO-TEN-003-F05, Critical, Seyi + Zainab independently): the configured
/// secret itself must meet a canonical minimum length, not just "non-empty" - the same
/// TD-PROVENANCE-DUMMYKEY-SWEEP shape already proven across 14 other files.
/// </summary>
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
    public void An_empty_secret_is_rejected_the_same_as_too_short()
    {
        var store = new HmacActorProvenanceStore();
        store.Configure(string.Empty);

        Assert.False(store.Verify("per_actor", Proof(SecretOfLength(64), "per_actor")));
    }

    [Fact]
    public void A_proof_computed_under_a_different_secret_never_verifies_even_with_a_valid_length_configured_secret()
    {
        var store = new HmacActorProvenanceStore();
        var configuredSecret = SecretOfLength(HmacActorProvenanceStore.MinimumSecretLength);
        store.Configure(configuredSecret);

        var wrongProof = Proof(SecretOfLength(HmacActorProvenanceStore.MinimumSecretLength) + "x", "per_actor");

        Assert.False(store.Verify("per_actor", wrongProof));
    }

    [Fact]
    public void Malformed_hex_never_verifies_even_against_a_correctly_configured_secret()
    {
        var store = new HmacActorProvenanceStore();
        store.Configure(SecretOfLength(HmacActorProvenanceStore.MinimumSecretLength));

        Assert.False(store.Verify("per_actor", "not-valid-hex"));
    }
}

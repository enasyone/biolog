using System.Security.Cryptography;
using System.Text;
using Biolog.Features.BiometricRetention.Api;
using Xunit;

namespace Biolog.Features.BiometricRetention.Tests;

/// <summary>Round 4 (god's dispatch, canonical pattern matching BIO-INT-003's own
/// <c>ActorProvenanceGate.cs</c>): a secret shorter than <see cref="HmacActorProvenanceStore.MinimumSecretLength"/>
/// must be treated exactly like no secret at all - never a weaker-but-still-usable key.</summary>
public sealed class ActorProvenanceGateTests
{
    private const string ActorId = "actor-1";

    private static string ComputeProof(byte[] key, string actorId) =>
        Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(actorId)));

    [Fact]
    public void Verify_rejects_every_proof_when_never_configured()
    {
        var store = new HmacActorProvenanceStore();

        var forged = ComputeProof(SHA256.HashData(Encoding.UTF8.GetBytes("\0no-provenance-secret-configured\0")), ActorId);

        Assert.False(store.Verify(ActorId, forged));
    }

    [Fact]
    public void Verify_rejects_every_proof_when_configured_below_the_minimum_secret_length()
    {
        var store = new HmacActorProvenanceStore();
        var shortSecret = new string('x', HmacActorProvenanceStore.MinimumSecretLength - 1);
        store.Configure(shortSecret);

        var proof = ComputeProof(Encoding.UTF8.GetBytes(shortSecret), ActorId);

        Assert.False(store.Verify(ActorId, proof));
    }

    [Fact]
    public void Verify_accepts_a_correct_proof_once_configured_at_exactly_the_minimum_length()
    {
        var store = new HmacActorProvenanceStore();
        var secret = new string('x', HmacActorProvenanceStore.MinimumSecretLength);
        store.Configure(secret);

        var proof = ComputeProof(Encoding.UTF8.GetBytes(secret), ActorId);

        Assert.True(store.Verify(ActorId, proof));
    }

    [Fact]
    public void Verify_rejects_a_proof_computed_for_a_different_actor_id()
    {
        var store = new HmacActorProvenanceStore();
        var secret = new string('x', HmacActorProvenanceStore.MinimumSecretLength);
        store.Configure(secret);

        var proofForSomeoneElse = ComputeProof(Encoding.UTF8.GetBytes(secret), "a-different-actor");

        Assert.False(store.Verify(ActorId, proofForSomeoneElse));
    }
}

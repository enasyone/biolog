// Developer tests for ADR-0004 Decision 4 compliance (BIO-CRD-001's write endpoints).
using Biolog.Api.Features.BrandAssets;
using Xunit;

namespace Biolog.UnitTests.Features.BrandAssets;

public sealed class IdempotencyStoreTests
{
    [Fact]
    public void First_claim_for_a_key_wins()
    {
        var store = new InMemoryIdempotencyStore();

        var (won, existing) = store.Claim("ten_a", "op", "key-1", "fingerprint-1");

        Assert.True(won);
        Assert.Null(existing);
    }

    [Fact]
    public void A_second_claim_for_the_same_key_loses_and_sees_the_in_flight_record()
    {
        var store = new InMemoryIdempotencyStore();
        store.Claim("ten_a", "op", "key-1", "fingerprint-1");

        var (won, existing) = store.Claim("ten_a", "op", "key-1", "fingerprint-1");

        Assert.False(won);
        Assert.NotNull(existing);
        Assert.Equal(IdempotencyRecordState.InFlight, existing!.State);
    }

    [Fact]
    public void Complete_transitions_to_done_and_is_visible_via_get()
    {
        var store = new InMemoryIdempotencyStore();
        store.Claim("ten_a", "op", "key-1", "fingerprint-1");

        store.Complete("ten_a", "op", "key-1", 201, "{\"ok\":true}");

        var record = store.Get("ten_a", "op", "key-1");
        Assert.Equal(IdempotencyRecordState.Done, record!.State);
        Assert.Equal(201, record.ResponseStatus);
    }

    [Fact]
    public void Release_frees_the_key_for_a_fresh_claim()
    {
        var store = new InMemoryIdempotencyStore();
        store.Claim("ten_a", "op", "key-1", "fingerprint-1");

        store.Release("ten_a", "op", "key-1");
        var (won, _) = store.Claim("ten_a", "op", "key-1", "fingerprint-1");

        Assert.True(won);
    }

    [Fact]
    public void Scope_key_cannot_be_aliased_by_a_delimiter_inside_operation_or_key()
    {
        var store = new InMemoryIdempotencyStore();
        store.Claim("ten_a", "card", "activate:key", "fp-1");

        // A naive `${tenant}:${operation}:${key}` join would collide with the claim above.
        var (won, existing) = store.Claim("ten_a", "card:activate", "key", "fp-2");

        Assert.True(won);
        Assert.Null(existing);
    }

    [Fact]
    public void Concurrent_claims_for_the_same_key_have_exactly_one_winner()
    {
        var store = new InMemoryIdempotencyStore();
        var wins = 0;

        Parallel.For(0, 50, _ =>
        {
            var (won, _) = store.Claim("ten_a", "op", "shared-key", "fp");
            if (won)
            {
                Interlocked.Increment(ref wins);
            }
        });

        Assert.Equal(1, wins);
    }
}

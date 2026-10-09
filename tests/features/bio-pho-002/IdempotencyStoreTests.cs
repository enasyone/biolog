using Biolog.Features.PhotoBooth.Domain;
using Xunit;

namespace Biolog.Features.PhotoBooth.Tests;

public sealed class IdempotencyStoreTests
{
    [Fact]
    public void Claim_on_a_fresh_key_returns_a_new_in_flight_record_and_is_the_new_claims_owner()
    {
        var store = new InMemoryIdempotencyStore();

        var claim = store.Claim("ten_a", "op", "key-1", "fingerprint-1");

        Assert.Equal(IdempotencyRecordState.InFlight, claim.Record.State);
        Assert.Equal("fingerprint-1", claim.Record.RequestFingerprint);
        Assert.True(claim.IsNewClaim);
    }

    [Fact]
    public void Claim_on_an_already_claimed_key_returns_the_existing_record_and_is_NOT_the_owner()
    {
        // TD-PHO-002-IDEMPOTENCY-RACE's own remediation: IsNewClaim is the ownership signal a
        // caller MUST branch on - the record's own State alone cannot distinguish "I just won
        // this claim" from "someone else's claim is already in flight" (both read InFlight).
        var store = new InMemoryIdempotencyStore();
        store.Claim("ten_a", "op", "key-1", "fingerprint-1");

        var second = store.Claim("ten_a", "op", "key-1", "fingerprint-1");

        Assert.Equal(IdempotencyRecordState.InFlight, second.Record.State);
        Assert.False(second.IsNewClaim);
    }

    [Fact]
    public void Complete_then_Get_returns_the_completed_result()
    {
        var store = new InMemoryIdempotencyStore();
        store.Claim("ten_a", "op", "key-1", "fingerprint-1");

        store.Complete("ten_a", "op", "key-1", "the-result");

        var record = store.Get("ten_a", "op", "key-1");
        Assert.NotNull(record);
        Assert.Equal(IdempotencyRecordState.Completed, record!.State);
        Assert.Equal("the-result", record.Result);
    }

    [Fact]
    public void Release_removes_an_in_flight_claim_so_a_genuine_retry_can_proceed()
    {
        var store = new InMemoryIdempotencyStore();
        store.Claim("ten_a", "op", "key-1", "fingerprint-1");

        store.Release("ten_a", "op", "key-1");

        Assert.Null(store.Get("ten_a", "op", "key-1"));
    }

    [Fact]
    public void The_same_key_under_a_different_tenant_or_operation_is_a_wholly_separate_claim()
    {
        var store = new InMemoryIdempotencyStore();
        store.Claim("ten_a", "op", "key-1", "fingerprint-1");
        store.Complete("ten_a", "op", "key-1", "result-a");

        Assert.Null(store.Get("ten_b", "op", "key-1"));
        Assert.Null(store.Get("ten_a", "other_op", "key-1"));
    }

    [Fact]
    public void Scope_key_construction_is_injective_across_delimiter_ambiguous_inputs()
    {
        // A naive delimiter-joined key ("a|b|c") would collide for ("a", "b|c") vs ("a|b", "c").
        // The JSON-array-based scope key must not.
        var store = new InMemoryIdempotencyStore();
        var claimA = store.Claim("a", "b|c", "key", "fp-1");
        var claimB = store.Claim("a|b", "c", "key", "fp-2");

        Assert.NotEqual(claimA.Record.RequestFingerprint, claimB.Record.RequestFingerprint);
        Assert.Equal("fp-1", store.Get("a", "b|c", "key")!.RequestFingerprint);
        Assert.Equal("fp-2", store.Get("a|b", "c", "key")!.RequestFingerprint);
    }
}

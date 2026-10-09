using Biolog.Features.ApiSecurity;
using Xunit;

namespace Biolog.Features.ApiSecurity.Tests;

public sealed class RateLimitingTests
{
    [Fact]
    public void Requests_within_the_limit_are_allowed()
    {
        var limiter = new InMemoryApiRateLimiter(limitPerWindow: 3, windowSeconds: 60);

        var first = limiter.TryAcquire("ten_a", "client-1", "oauth:token");
        var second = limiter.TryAcquire("ten_a", "client-1", "oauth:token");
        var third = limiter.TryAcquire("ten_a", "client-1", "oauth:token");

        Assert.True(first.Allowed);
        Assert.True(second.Allowed);
        Assert.True(third.Allowed);
        Assert.Equal(0, third.RemainingInWindow);
    }

    [Fact]
    public void A_request_beyond_the_limit_is_rejected_with_a_positive_retry_after()
    {
        var limiter = new InMemoryApiRateLimiter(limitPerWindow: 2, windowSeconds: 60);

        limiter.TryAcquire("ten_a", "client-1", "oauth:token");
        limiter.TryAcquire("ten_a", "client-1", "oauth:token");
        var rejected = limiter.TryAcquire("ten_a", "client-1", "oauth:token");

        Assert.False(rejected.Allowed);
        Assert.True(rejected.RetryAfterSeconds > 0);
    }

    [Fact]
    public void Different_clients_in_the_same_tenant_have_independent_buckets()
    {
        var limiter = new InMemoryApiRateLimiter(limitPerWindow: 1, windowSeconds: 60);

        var clientA = limiter.TryAcquire("ten_a", "client-1", "oauth:token");
        var clientB = limiter.TryAcquire("ten_a", "client-2", "oauth:token");

        Assert.True(clientA.Allowed);
        Assert.True(clientB.Allowed);
    }

    [Fact]
    public void Different_routes_for_the_same_client_have_independent_buckets()
    {
        var limiter = new InMemoryApiRateLimiter(limitPerWindow: 1, windowSeconds: 60);

        var tokenRoute = limiter.TryAcquire("ten_a", "client-1", "oauth:token");
        var whoamiRoute = limiter.TryAcquire("ten_a", "client-1", "service-accounts:whoami");

        Assert.True(tokenRoute.Allowed);
        Assert.True(whoamiRoute.Allowed);
    }

    [Fact]
    public void Field_combinations_that_would_collide_under_naive_string_concatenation_do_not_share_a_bucket()
    {
        // BIO-INT-002's own remediation history (docs/features/bio-int-002/README.md) found that
        // a bucket key built by joining fields without a collision-proof separator can let two
        // different logical buckets collide - e.g. tenant "a|b" + client "c" vs tenant "a" +
        // client "b|c" would hash/stringify identically under naive "tenant|client" concatenation.
        // RateLimitKey is a value-equality record struct, not a joined string, so this must not
        // happen here.
        var limiter = new InMemoryApiRateLimiter(limitPerWindow: 1, windowSeconds: 60);

        var first = limiter.TryAcquire("a|b", "c", "route");
        var second = limiter.TryAcquire("a", "b|c", "route");

        Assert.True(first.Allowed);
        Assert.True(second.Allowed, "a different logical (tenant, client) pair must get its own bucket, not collide with the first.");
    }

    [Fact]
    public void A_window_reset_restores_capacity()
    {
        var limiter = new InMemoryApiRateLimiter(limitPerWindow: 1, windowSeconds: 0);

        var first = limiter.TryAcquire("ten_a", "client-1", "oauth:token");
        // windowSeconds=0 means every subsequent check is already past the window boundary.
        var second = limiter.TryAcquire("ten_a", "client-1", "oauth:token");

        Assert.True(first.Allowed);
        Assert.True(second.Allowed);
    }
}

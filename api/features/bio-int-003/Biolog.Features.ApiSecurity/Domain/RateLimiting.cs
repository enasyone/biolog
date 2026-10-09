namespace Biolog.Features.ApiSecurity;

/// <summary>
/// BIO-INT-003-AC03: a fixed-window rate limit decision. <see cref="RetryAfterSeconds"/> is
/// only meaningful when <see cref="Allowed"/> is <c>false</c> - the caller surfaces it as the
/// HTTP <c>Retry-After</c> header on a 429 (ADR-0004's problem-response envelope).
/// </summary>
public sealed record RateLimitDecision(bool Allowed, int RetryAfterSeconds, int LimitPerWindow, int RemainingInWindow);

/// <summary>
/// Tenant+client+route-scoped fixed-window rate limiter. Keyed by a value-equality record
/// struct (<see cref="RateLimitKey"/>), not a string concatenation - BIO-INT-002's own
/// remediation history (docs/features/bio-int-002/README.md) found that a bucket key built by
/// naively joining fields without a collision-proof separator can let two different logical
/// buckets collide (e.g. tenant "a|b" + client "c" vs tenant "a" + client "b|c"). A record
/// struct's value equality has no such ambiguity - there is no delimiter to get wrong.
/// </summary>
public interface IApiRateLimiter
{
    RateLimitDecision TryAcquire(string tenantId, string clientId, string route);
}

public readonly record struct RateLimitKey(string TenantId, string ClientId, string Route);

internal sealed class RateLimitBucket
{
    public DateTimeOffset WindowStart;
    public int Count;
}

/// <summary>
/// In-memory fixed-window limiter. Each bucket's own lock (not a single global lock) is
/// acquired only around the claim/increment/reset critical section, and every return path is
/// inside the lock - no throw-path between "window checked" and "count updated" can leave a
/// bucket in a half-updated state (the exception-safety lesson BIO-INT-002's remediation
/// history calls out explicitly for any shared counter store).
/// </summary>
public sealed class InMemoryApiRateLimiter(int limitPerWindow = 60, int windowSeconds = 60) : IApiRateLimiter
{
    private readonly Dictionary<RateLimitKey, RateLimitBucket> _buckets = [];
    private readonly Lock _gate = new();

    public RateLimitDecision TryAcquire(string tenantId, string clientId, string route)
    {
        var key = new RateLimitKey(tenantId, clientId, route);
        var now = DateTimeOffset.UtcNow;

        lock (_gate)
        {
            if (!_buckets.TryGetValue(key, out var bucket))
            {
                bucket = new RateLimitBucket { WindowStart = now, Count = 0 };
                _buckets[key] = bucket;
            }

            if (now - bucket.WindowStart >= TimeSpan.FromSeconds(windowSeconds))
            {
                bucket.WindowStart = now;
                bucket.Count = 0;
            }

            if (bucket.Count >= limitPerWindow)
            {
                var retryAfter = (int)Math.Ceiling((bucket.WindowStart + TimeSpan.FromSeconds(windowSeconds) - now).TotalSeconds);
                return new RateLimitDecision(Allowed: false, RetryAfterSeconds: Math.Max(retryAfter, 1), LimitPerWindow: limitPerWindow, RemainingInWindow: 0);
            }

            bucket.Count += 1;
            return new RateLimitDecision(Allowed: true, RetryAfterSeconds: 0, LimitPerWindow: limitPerWindow, RemainingInWindow: limitPerWindow - bucket.Count);
        }
    }
}

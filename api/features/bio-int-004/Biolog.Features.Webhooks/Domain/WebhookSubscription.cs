using System.Net;

namespace Biolog.Features.Webhooks;

/// <summary>
/// BIO-INT-004-AC01: tenant-scoped, filtered. A subscription names the EXACT set of
/// <see cref="AggregateType"/> values it wants delivered - "selected events," per the
/// approved user story, never an implicit subscribe-to-everything default. Optimistic
/// concurrency (<see cref="Version"/>) mirrors BIO-INT-003's own
/// <c>ServiceAccount.Version</c>/BIO-INT-001's <c>AuthProviderConfig</c> convention.
///
/// <b><see cref="SigningSecretHex"/> is deliberately stored in USABLE, plaintext-equivalent
/// form - not hashed the way BIO-INT-003's own <c>ServiceAccount.SecretHash</c> is.</b> This
/// is not an inconsistency with that precedent; it is the opposite role a secret plays here.
/// A service-account secret is a credential the CALLER presents to US - we only ever need to
/// verify a hash of what they send, never the plaintext back, so hashing is correct there.
/// A webhook signing secret is OUR OWN key material - WE compute
/// <c>hex(HMACSHA256(secret, body))</c> on every outbound delivery, the same shared-secret
/// role every <c>*ActorProvenanceSecret</c>/<c>WebSessionBridge:TokenSecret</c> config value
/// already has in this codebase (the HOLDER of an HMAC key always needs it in usable form;
/// nobody "verifies a hash" of a key you compute with yourself). Returned to the caller
/// exactly once, at creation (so the tenant can configure their own receiving endpoint to
/// verify against the identical value), never again afterward - same "plaintext shown once"
/// discipline BIO-INT-003's own service-account secret already established, just applied to
/// a key WE retain rather than one WE only ever hash.
/// </summary>
public sealed record WebhookSubscription
{
    public string SubscriptionId { get; }
    public string TenantId { get; }
    public Uri Url { get; }
    public IReadOnlySet<AggregateType> EventTypes { get; }
    public bool Enabled { get; }
    public string SigningSecretHex { get; }
    public int Version { get; }
    public DateTimeOffset CreatedAt { get; }

    public WebhookSubscription(
        string subscriptionId, string tenantId, Uri url, IReadOnlySet<AggregateType> eventTypes,
        bool enabled, string signingSecretHex, int version, DateTimeOffset createdAt)
    {
        if (eventTypes.Count == 0)
        {
            throw new ArgumentException("a webhook subscription must select at least one event type", nameof(eventTypes));
        }

        // BIO-INT-004-ZB-01 (Zainab, High - SSRF): a best-effort, NON-authoritative pre-check -
        // reject a literal loopback/private/link-local IP address in the URL at creation time, so
        // an obviously-bad subscription fails fast with a clear error instead of silently
        // dead-lettering every delivery forever. This is deliberately NOT the real control: a
        // hostname's DNS resolution can change between now and delivery time (and isn't resolved
        // here at all, to avoid a DNS lookup inside the create request), so the authoritative
        // enforcement is WebhookConnectCallback's own connect-time check against the SAME
        // WebhookDestinationPolicy, which this cannot be bypassed by skipping.
        if (url.HostNameType is UriHostNameType.IPv4 or UriHostNameType.IPv6 &&
            IPAddress.TryParse(url.Host, out var literalAddress) &&
            !WebhookDestinationPolicy.IsAllowed(literalAddress, out var blockedReason))
        {
            throw new ArgumentException($"webhook URL's host '{url.Host}' is a disallowed destination ({blockedReason})", nameof(url));
        }

        SubscriptionId = subscriptionId;
        TenantId = tenantId;
        Url = url;
        EventTypes = eventTypes;
        Enabled = enabled;
        SigningSecretHex = signingSecretHex;
        Version = version;
        CreatedAt = createdAt;
    }

    public WebhookSubscription WithEnabled(bool enabled, int newVersion) =>
        new(SubscriptionId, TenantId, Url, EventTypes, enabled, SigningSecretHex, newVersion, CreatedAt);
}

public interface IWebhookSubscriptionRepository
{
    Task AddAsync(WebhookSubscription subscription, CancellationToken cancellationToken);

    Task<WebhookSubscription?> GetAsync(string tenantId, string subscriptionId, CancellationToken cancellationToken);

    /// <summary>Tenant-scoped (AC01) - never returns a subscription belonging to a different tenant.</summary>
    Task<IReadOnlyList<WebhookSubscription>> ListForTenantAsync(string tenantId, CancellationToken cancellationToken);

    /// <summary>Tenant-scoped AND filtered (AC01) - only enabled subscriptions whose <see cref="WebhookSubscription.EventTypes"/> contains the given type.</summary>
    Task<IReadOnlyList<WebhookSubscription>> ListMatchingAsync(string tenantId, AggregateType eventType, CancellationToken cancellationToken);

    Task UpdateAsync(WebhookSubscription subscription, CancellationToken cancellationToken);
}

/// <summary>Synthetic/demo-foundation in-memory store - same rigor level as every other
/// `InMemory*` store in this codebase for a story with no persistence requirement of its own.</summary>
public sealed class InMemoryWebhookSubscriptionRepository : IWebhookSubscriptionRepository
{
    private readonly Dictionary<(string TenantId, string SubscriptionId), WebhookSubscription> _subscriptions = [];
    private readonly Lock _gate = new();

    public Task AddAsync(WebhookSubscription subscription, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _subscriptions[(subscription.TenantId, subscription.SubscriptionId)] = subscription;
        }

        return Task.CompletedTask;
    }

    public Task<WebhookSubscription?> GetAsync(string tenantId, string subscriptionId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            // Not-found-not-forbidden across tenants (BIO-INT-003's own convention): a
            // subscription id that belongs to a DIFFERENT tenant is indistinguishable from
            // one that does not exist at all - the compound key enforces this by construction.
            return Task.FromResult(_subscriptions.GetValueOrDefault((tenantId, subscriptionId)));
        }
    }

    public Task<IReadOnlyList<WebhookSubscription>> ListForTenantAsync(string tenantId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            IReadOnlyList<WebhookSubscription> result = _subscriptions.Values.Where(s => s.TenantId == tenantId).ToList();
            return Task.FromResult(result);
        }
    }

    public Task<IReadOnlyList<WebhookSubscription>> ListMatchingAsync(string tenantId, AggregateType eventType, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            IReadOnlyList<WebhookSubscription> result = _subscriptions.Values
                .Where(s => s.TenantId == tenantId && s.Enabled && s.EventTypes.Contains(eventType))
                .ToList();
            return Task.FromResult(result);
        }
    }

    public Task UpdateAsync(WebhookSubscription subscription, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var key = (subscription.TenantId, subscription.SubscriptionId);
            if (!_subscriptions.ContainsKey(key))
            {
                throw new InvalidOperationException($"no subscription '{subscription.SubscriptionId}' for tenant '{subscription.TenantId}'");
            }

            _subscriptions[key] = subscription;
        }

        return Task.CompletedTask;
    }
}

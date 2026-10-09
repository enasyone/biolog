using Biolog.Features.Webhooks;
using Xunit;

namespace Biolog.Features.Webhooks.Tests;

/// <summary>Grants every permission by default; add a key to <see cref="DeniedPermissionKeys"/> to simulate an actor who lacks it.</summary>
internal sealed class FakePermissionGateway : IPermissionGateway
{
    public HashSet<string> DeniedPermissionKeys { get; } = [];

    public Task<bool> ActorHasPermissionAsync(string tenantId, string actorId, string permissionKey, CancellationToken cancellationToken = default) =>
        Task.FromResult(!DeniedPermissionKeys.Contains(permissionKey));
}

/// <summary>BIO-INT-004-AC01: "Tenants configure webhook subscriptions with event-type filters."</summary>
public sealed class WebhookSubscriptionServiceTests
{
    private static readonly AuditActor TestActor = new(ActorType.User, "per_test_actor");

    private static WebhookSubscriptionService Build(
        IWebhookSubscriptionRepository? repository = null, IPermissionGateway? permissionGateway = null, bool allowInsecureHttp = false) =>
        new(repository ?? new InMemoryWebhookSubscriptionRepository(), permissionGateway ?? new FakePermissionGateway(), allowInsecureHttp);

    private static Uri Url => new("https://example-synthetic.test/webhooks/inbound");

    [Fact]
    public async Task Creating_a_subscription_with_no_event_types_is_rejected()
    {
        var service = Build();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateAsync("ten_a", TestActor, Url, new HashSet<AggregateType>(), CancellationToken.None));
    }

    [Theory]
    [InlineData("http://127.0.0.1/hook")]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("http://10.0.0.5:8080/hook")]
    [InlineData("http://[::1]/hook")]
    public async Task Creating_a_subscription_with_a_literal_loopback_or_private_ip_url_is_rejected(string url)
    {
        // BIO-INT-004-ZB-01 (Zainab, High - SSRF): a best-effort, non-authoritative pre-check -
        // see WebhookSubscription's own constructor comment for why a hostname (not covered here)
        // still needs the real connect-time check in WebhookConnectCallback.
        var service = Build();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateAsync("ten_a", TestActor, new Uri(url), new HashSet<AggregateType> { AggregateType.Device }, CancellationToken.None));
    }

    /// <summary>BIO-INT-004-ZB-04 (Zainab, High, round 2): HMAC signing proves integrity, not
    /// confidentiality - plain HTTP would expose personal/sensitive event payloads to any network
    /// observer. HTTPS is required unless the purpose-built, config-sourced `allowInsecureHttp`
    /// flag is explicitly set (never an ASPNETCORE_ENVIRONMENT/IsDevelopment() check - see
    /// WebhookSubscriptionService.CreateAsync's own comment for why).</summary>
    [Fact]
    public async Task Creating_a_subscription_with_a_plain_http_url_is_rejected_by_default()
    {
        var service = Build();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateAsync("ten_a", TestActor, new Uri("http://example-synthetic.test/hook"), new HashSet<AggregateType> { AggregateType.Device }, CancellationToken.None));
    }

    [Fact]
    public async Task Creating_a_subscription_with_a_plain_http_url_succeeds_when_insecure_http_is_explicitly_allowed()
    {
        var service = Build(allowInsecureHttp: true);

        var subscription = await service.CreateAsync(
            "ten_a", TestActor, new Uri("http://example-synthetic.test/hook"), new HashSet<AggregateType> { AggregateType.Device }, CancellationToken.None);

        Assert.Equal("http", subscription.Url.Scheme);
    }

    [Fact]
    public async Task Creating_a_subscription_with_an_https_url_always_succeeds_regardless_of_the_insecure_http_flag()
    {
        var service = Build(allowInsecureHttp: false);

        var subscription = await service.CreateAsync("ten_a", TestActor, Url, new HashSet<AggregateType> { AggregateType.Device }, CancellationToken.None);

        Assert.Equal("https", subscription.Url.Scheme);
    }

    [Fact]
    public async Task A_created_subscription_is_enabled_by_default_with_a_fresh_random_signing_secret()
    {
        var service = Build();

        var subscription = await service.CreateAsync(
            "ten_a", TestActor, Url, new HashSet<AggregateType> { AggregateType.DocumentSession }, CancellationToken.None);

        Assert.True(subscription.Enabled);
        Assert.Equal(1, subscription.Version);
        Assert.Matches("^[0-9a-f]{64}$", subscription.SigningSecretHex);
    }

    [Fact]
    public async Task Two_created_subscriptions_never_share_the_same_signing_secret()
    {
        var service = Build();

        var a = await service.CreateAsync("ten_a", TestActor, Url, new HashSet<AggregateType> { AggregateType.Device }, CancellationToken.None);
        var b = await service.CreateAsync("ten_a", TestActor, Url, new HashSet<AggregateType> { AggregateType.Device }, CancellationToken.None);

        Assert.NotEqual(a.SigningSecretHex, b.SigningSecretHex);
    }

    [Fact]
    public async Task A_created_subscription_is_only_visible_to_its_own_tenant()
    {
        var service = Build();
        await service.CreateAsync("ten_a", TestActor, Url, new HashSet<AggregateType> { AggregateType.Device }, CancellationToken.None);
        await service.CreateAsync("ten_b", TestActor, Url, new HashSet<AggregateType> { AggregateType.Person }, CancellationToken.None);

        var tenantA = await service.ListAsync("ten_a", TestActor, CancellationToken.None);
        var tenantB = await service.ListAsync("ten_b", TestActor, CancellationToken.None);

        Assert.Single(tenantA);
        Assert.Equal(AggregateType.Device, Assert.Single(tenantA[0].EventTypes));
        Assert.Single(tenantB);
        Assert.Equal(AggregateType.Person, Assert.Single(tenantB[0].EventTypes));
    }

    [Fact]
    public async Task Create_requires_the_manage_permission()
    {
        var gateway = new FakePermissionGateway();
        gateway.DeniedPermissionKeys.Add(WebhookSubscriptionService.ManagePermission);
        var service = Build(permissionGateway: gateway);

        await Assert.ThrowsAsync<PermissionRequiredException>(() =>
            service.CreateAsync("ten_a", TestActor, Url, new HashSet<AggregateType> { AggregateType.Device }, CancellationToken.None));
    }

    [Fact]
    public async Task List_requires_the_read_permission()
    {
        var gateway = new FakePermissionGateway();
        gateway.DeniedPermissionKeys.Add(WebhookSubscriptionService.ReadPermission);
        var service = Build(permissionGateway: gateway);

        await Assert.ThrowsAsync<PermissionRequiredException>(() =>
            service.ListAsync("ten_a", TestActor, CancellationToken.None));
    }

    [Fact]
    public async Task Disabling_a_subscription_takes_it_out_of_dispatch_matching()
    {
        var repository = new InMemoryWebhookSubscriptionRepository();
        var service = Build(repository: repository);
        var subscription = await service.CreateAsync(
            "ten_a", TestActor, Url, new HashSet<AggregateType> { AggregateType.Device }, CancellationToken.None);

        var updated = await service.SetEnabledAsync("ten_a", TestActor, subscription.SubscriptionId, enabled: false, subscription.Version, CancellationToken.None);

        Assert.False(updated.Enabled);
        Assert.Equal(2, updated.Version);
        var matching = await repository.ListMatchingAsync("ten_a", AggregateType.Device, CancellationToken.None);
        Assert.Empty(matching);
    }

    [Fact]
    public async Task Setting_enabled_with_a_stale_version_is_rejected_as_a_concurrency_conflict()
    {
        var service = Build();
        var subscription = await service.CreateAsync(
            "ten_a", TestActor, Url, new HashSet<AggregateType> { AggregateType.Device }, CancellationToken.None);
        await service.SetEnabledAsync("ten_a", TestActor, subscription.SubscriptionId, enabled: false, subscription.Version, CancellationToken.None);

        var ex = await Assert.ThrowsAsync<SubscriptionConcurrencyConflictException>(() =>
            service.SetEnabledAsync("ten_a", TestActor, subscription.SubscriptionId, enabled: true, subscription.Version, CancellationToken.None));

        Assert.Equal(2, ex.CurrentVersion);
    }

    [Fact]
    public async Task Setting_enabled_for_an_unknown_subscription_id_is_not_found()
    {
        var service = Build();

        await Assert.ThrowsAsync<SubscriptionNotFoundException>(() =>
            service.SetEnabledAsync("ten_a", TestActor, "whs_does-not-exist", enabled: true, 1, CancellationToken.None));
    }

    [Fact]
    public async Task A_subscription_from_a_different_tenant_cannot_be_toggled_not_found_not_forbidden()
    {
        var service = Build();
        var subscription = await service.CreateAsync(
            "ten_a", TestActor, Url, new HashSet<AggregateType> { AggregateType.Device }, CancellationToken.None);

        await Assert.ThrowsAsync<SubscriptionNotFoundException>(() =>
            service.SetEnabledAsync("ten_b", TestActor, subscription.SubscriptionId, enabled: false, subscription.Version, CancellationToken.None));
    }
}

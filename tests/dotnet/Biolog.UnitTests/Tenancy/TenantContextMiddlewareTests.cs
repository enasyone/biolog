using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Biolog.Api.Tenancy;
using Biolog.Features.TenantDomainRegistry;
using Biolog.Features.TenantDomainRegistry.Api;
using Biolog.Features.TenantDomainRegistry.Audit;
using Biolog.Features.TenantDomainRegistry.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Biolog.UnitTests.Tenancy;

/// <summary>
/// Developer tests for BIO-FND-008-AC04 / BIO-TEN-002's host-channel wiring. These construct
/// a real <see cref="TenantDomainService"/> (with its production in-memory registry and audit
/// sink, not a fake) and drive the actual <see cref="TenantContextMiddleware"/> class that
/// Program.cs registers into the packaged host - the same class, exercised directly rather
/// than through a full HTTP round-trip. <c>Biolog.IntegrationTests.TenantContextHostChannelTests</c>
/// covers the complementary concern this file cannot: that <see cref="TenantDomainService"/>
/// actually resolves from the real DI container inside the packaged
/// <c>WebApplicationFactory&lt;Program&gt;</c> pipeline, not just when constructed by hand.
/// </summary>
public sealed class TenantContextMiddlewareTests
{
    private static readonly AuditActor Actor = new(ActorType.User, "admin_alice");
    private const string TenantId = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77";

    private static TenantDomainService NewService() => new(new InMemoryTenantDomainRegistry(), new InMemoryTenantDomainAuditSink());

    private static async Task<TenantDomainService> ServiceWithVerifiedDomain(string hostname, CancellationToken ct)
    {
        var service = NewService();
        var registered = await service.RegisterAsync(
            new RegisterDomainRequest { Hostname = hostname, TenantId = TenantId }, Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a79", ct);
        await service.UpdateStatusAsync(
            registered.Hostname, new UpdateDomainStatusRequest { Version = registered.Version, Status = "VERIFIED" }, Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a80", ct);
        return service;
    }

    private static HttpContext HttpContextForHost(string host)
    {
        var context = new DefaultHttpContext();
        context.Request.Host = new HostString(host);
        return context;
    }

    private const string DemoSecret = "this-is-a-synthetic-demo-session-bridge-secret-32plus";
    private const string DemoSyntheticTenantId = "ten_01a113cb-0d05-774b-a463-a954598ed2e9";

    /// <summary>Unconfigured - never activates, matching every pre-existing test's own expectation that only the host channel resolves anything.</summary>
    private static DemoSessionTenantResolver NewUnconfiguredDemoSessionResolver() =>
        DemoSessionTenantResolver.LoadFromConfiguration(new ConfigurationBuilder().Build());

    private static DemoSessionTenantResolver NewConfiguredDemoSessionResolver(string? secret = DemoSecret, string? tenantId = DemoSyntheticTenantId) =>
        DemoSessionTenantResolver.LoadFromConfiguration(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["WebSessionBridge:TokenSecret"] = secret,
                ["WebSessionBridge:SyntheticTenantId"] = tenantId,
            })
            .Build());

    /// <summary>Signs a token exactly the way web/lib/session-token.ts does - mirrored here, not imported, since this is a cross-stack (TS signer / C# verifier) boundary by design.</summary>
    private static string SignDemoSessionToken(string secret, object claims)
    {
        var json = JsonSerializer.Serialize(claims);
        var payloadB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        var signature = Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(payloadB64)));
        return $"{payloadB64}.{signature}";
    }

    private static object ValidDemoClaims(long? expiresAtMs = null) => new
    {
        sid = "sid_test_1",
        roleId = "admin",
        tenantId = "alpha",
        issuedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        expiresAtMs = expiresAtMs ?? DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds(),
    };

    [Fact]
    public async Task Leaves_context_null_when_the_request_carries_no_tenant_information()
    {
        var accessor = new TenantContextAccessor();
        var middleware = new TenantContextMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(new DefaultHttpContext(), accessor, NewService(), NewUnconfiguredDemoSessionResolver());

        Assert.Null(accessor.Context);
    }

    [Theory]
    [InlineData("X-Tenant-Id", "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77")]
    public async Task Does_not_invent_a_default_tenant_even_when_a_header_looks_like_one(string headerName, string headerValue)
    {
        // Only the real Host header may ever resolve a tenant (ADR-0002 Decision 1.1) - a
        // caller-controlled header must never become a channel.
        var accessor = new TenantContextAccessor();
        var middleware = new TenantContextMiddleware(_ => Task.CompletedTask);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers[headerName] = headerValue;

        await middleware.InvokeAsync(httpContext, accessor, NewService(), NewUnconfiguredDemoSessionResolver());

        Assert.Null(accessor.Context);
    }

    [Fact]
    public async Task A_verified_host_resolves_the_context_from_the_real_Host_header()
    {
        var ct = TestContext.Current.CancellationToken;
        var accessor = new TenantContextAccessor();
        var middleware = new TenantContextMiddleware(_ => Task.CompletedTask);
        var service = await ServiceWithVerifiedDomain("portal.example.edu.ng", ct);
        var httpContext = HttpContextForHost("portal.example.edu.ng");
        httpContext.RequestAborted = ct;

        await middleware.InvokeAsync(httpContext, accessor, service, NewUnconfiguredDemoSessionResolver());

        Assert.NotNull(accessor.Context);
        Assert.Equal(TenantId, accessor.Context!.TenantId);
        Assert.Equal(new[] { "host" }, accessor.Context.Channels);
    }

    [Fact]
    public async Task An_unknown_host_leaves_the_context_null()
    {
        var ct = TestContext.Current.CancellationToken;
        var accessor = new TenantContextAccessor();
        var middleware = new TenantContextMiddleware(_ => Task.CompletedTask);
        var httpContext = HttpContextForHost("nobody-registered-this.example.edu.ng");
        httpContext.RequestAborted = ct;

        await middleware.InvokeAsync(httpContext, accessor, NewService(), NewUnconfiguredDemoSessionResolver());

        Assert.Null(accessor.Context);
    }

    [Fact]
    public async Task An_unverified_pending_host_leaves_the_context_null()
    {
        // ADR-0002 Decision 1.3: only VERIFIED resolves. A hostname existing but not yet
        // verified must fail exactly like an unknown one - not a weaker, distinguishable case.
        var ct = TestContext.Current.CancellationToken;
        var accessor = new TenantContextAccessor();
        var middleware = new TenantContextMiddleware(_ => Task.CompletedTask);
        var service = NewService();
        await service.RegisterAsync(
            new RegisterDomainRequest { Hostname = "pending.example.edu.ng", TenantId = TenantId }, Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a81", ct);
        var httpContext = HttpContextForHost("pending.example.edu.ng");
        httpContext.RequestAborted = ct;

        await middleware.InvokeAsync(httpContext, accessor, service, NewUnconfiguredDemoSessionResolver());

        Assert.Null(accessor.Context);
    }

    [Fact]
    public async Task A_context_already_resolved_before_the_middleware_runs_makes_it_throw_rather_than_silently_swap_it()
    {
        // BIO-TEN-002-QA-03 (Seyi round 3) / ADR-0002 Decision 3 ("No setter, no swap"): the
        // accessor is set-once. If a scoped accessor already has a resolved context before
        // TenantContextMiddleware runs - a bug, or a tampering attempt via the concrete
        // accessor type - the middleware's own attempt to set the real one must fail loudly,
        // not silently overwrite whatever was already there. Silently overwriting is exactly
        // the "swap" Decision 3 forbids; this replaces a prior test that asserted the opposite
        // (silent overwrite) under the old, mutable-setter design.
        var accessor = new TenantContextAccessor();
        accessor.SetResolved(new TenantContext("ten_stale0000000000000000000000", "binding-1", new[] { "host" }));
        var middleware = new TenantContextMiddleware(_ => Task.CompletedTask);

        await Assert.ThrowsAsync<TenantContextImmutableException>(
            () => middleware.InvokeAsync(new DefaultHttpContext(), accessor, NewService(), NewUnconfiguredDemoSessionResolver()));
    }

    [Fact]
    public void TenantContext_construction_rejects_a_malformed_tenant_id()
    {
        // BIO-TEN-002-QA-03: construction-time defense-in-depth against a wrong identifier
        // type or an empty/forged value reaching TenantContext, on top of the access
        // restriction (internal constructor) that stops forgery from outside this assembly.
        Assert.Throws<ArgumentException>(() => new TenantContext("", "binding-1", []));
        Assert.Throws<ArgumentException>(() => new TenantContext("not-a-tenant-id", "binding-1", []));
        Assert.Throws<ArgumentException>(() => new TenantContext("usr_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77", "binding-1", []));
    }

    [Fact]
    public void Mutating_the_caller_s_channel_list_after_construction_does_not_change_the_context()
    {
        // BIO-TEN-002-QA-03: Channels must be a defensive copy, not a live view over whatever
        // list the caller happened to pass in - otherwise a reference the caller kept could
        // change this context's contents without ever going through the accessor.
        var mutableChannels = new List<string> { "host" };
        var context = new TenantContext(TenantId, "binding-1", mutableChannels);

        mutableChannels.Add("principal");

        Assert.Equal(new[] { "host" }, context.Channels);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)context.Channels).Add("service"));
    }

    [Fact]
    public async Task Calls_the_next_delegate()
    {
        var called = false;
        var middleware = new TenantContextMiddleware(_ =>
        {
            called = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(new DefaultHttpContext(), new TenantContextAccessor(), NewService(), NewUnconfiguredDemoSessionResolver());

        Assert.True(called);
    }

    // --- TD-WEB-PROVENANCE-BRIDGE round 5 (ZB-WEBPROV-02): demo-session tenant confinement ---

    [Fact]
    public async Task A_genuinely_signed_unexpired_demo_session_token_resolves_the_configured_synthetic_tenant_with_its_own_distinct_channel()
    {
        var accessor = new TenantContextAccessor();
        var middleware = new TenantContextMiddleware(_ => Task.CompletedTask);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Web-Session-Token"] = SignDemoSessionToken(DemoSecret, ValidDemoClaims());

        await middleware.InvokeAsync(httpContext, accessor, NewService(), NewConfiguredDemoSessionResolver());

        Assert.NotNull(accessor.Context);
        Assert.Equal(DemoSyntheticTenantId, accessor.Context!.TenantId);
        Assert.Equal(new[] { "demo-session" }, accessor.Context.Channels);
    }

    [Fact]
    public async Task A_verified_demo_session_marker_confines_resolution_to_the_synthetic_tenant_EVEN_WHEN_the_request_also_carries_a_real_verified_host()
    {
        // The whole point of round 5: a verified demo session must NEVER be able to reach a
        // real, Host-resolved tenant - checked first, unconditionally, not merely "if the host
        // doesn't resolve to anything else."
        var ct = TestContext.Current.CancellationToken;
        var accessor = new TenantContextAccessor();
        var middleware = new TenantContextMiddleware(_ => Task.CompletedTask);
        var service = await ServiceWithVerifiedDomain("portal.example.edu.ng", ct);
        var httpContext = HttpContextForHost("portal.example.edu.ng");
        httpContext.RequestAborted = ct;
        httpContext.Request.Headers["X-Web-Session-Token"] = SignDemoSessionToken(DemoSecret, ValidDemoClaims());

        await middleware.InvokeAsync(httpContext, accessor, service, NewConfiguredDemoSessionResolver());

        Assert.Equal(DemoSyntheticTenantId, accessor.Context!.TenantId);
        Assert.Equal(new[] { "demo-session" }, accessor.Context.Channels);
        Assert.NotEqual(TenantId, accessor.Context.TenantId);
    }

    [Fact]
    public async Task A_demo_session_token_claiming_beta_resolves_to_its_own_configured_tenant_not_alpha_s()
    {
        // OPS002-F01 fix: the claimed tenantId IS now consulted for resolution - "beta" reaches
        // a genuinely different tenant than "alpha" when WebSessionBridge:SyntheticTenantIdBeta
        // is configured (see DemoSessionTenantResolver's own header for the full account).
        const string betaTenantId = "ten_01a113cb-0d05-774b-a463-a954598ed2f0";
        var accessor = new TenantContextAccessor();
        var middleware = new TenantContextMiddleware(_ => Task.CompletedTask);
        var httpContext = new DefaultHttpContext();
        var claims = new { sid = "sid_x", roleId = "viewer", tenantId = "beta", issuedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), expiresAtMs = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds() };
        httpContext.Request.Headers["X-Web-Session-Token"] = SignDemoSessionToken(DemoSecret, claims);
        var resolver = DemoSessionTenantResolver.LoadFromConfiguration(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["WebSessionBridge:TokenSecret"] = DemoSecret,
                ["WebSessionBridge:SyntheticTenantId"] = DemoSyntheticTenantId,
                ["WebSessionBridge:SyntheticTenantIdBeta"] = betaTenantId,
            })
            .Build());

        await middleware.InvokeAsync(httpContext, accessor, NewService(), resolver);

        Assert.Equal(betaTenantId, accessor.Context!.TenantId);
        Assert.NotEqual(DemoSyntheticTenantId, accessor.Context.TenantId);
    }

    [Fact]
    public async Task SY_OPS002_03_a_demo_session_token_claiming_beta_falls_through_to_host_resolution_not_alpha_s_tenant_when_beta_is_unconfigured()
    {
        // Round 2 (SY-OPS002-03, High): round 1 had this claim silently resolve to alpha's
        // tenant when WebSessionBridge:SyntheticTenantIdBeta was unset - Seyi's review
        // correctly rejected that as the identical cosmetic-switch defect OPS002-F01 was
        // originally about. Now it is rejected outright (null from TryResolve), falling
        // through to the real Host-resolved tenant exactly like a tampered/expired marker
        // already does - see the two tests immediately below for that established shape.
        var ct = TestContext.Current.CancellationToken;
        var accessor = new TenantContextAccessor();
        var middleware = new TenantContextMiddleware(_ => Task.CompletedTask);
        var service = await ServiceWithVerifiedDomain("portal.example.edu.ng", ct);
        var httpContext = HttpContextForHost("portal.example.edu.ng");
        httpContext.RequestAborted = ct;
        var claims = new { sid = "sid_x", roleId = "viewer", tenantId = "beta", issuedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), expiresAtMs = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds() };
        httpContext.Request.Headers["X-Web-Session-Token"] = SignDemoSessionToken(DemoSecret, claims);

        // NewConfiguredDemoSessionResolver() only sets the primary key, never beta's.
        await middleware.InvokeAsync(httpContext, accessor, service, NewConfiguredDemoSessionResolver());

        Assert.Equal(TenantId, accessor.Context!.TenantId);
        Assert.Equal(new[] { "host" }, accessor.Context.Channels);
    }

    [Fact]
    public async Task A_tampered_demo_session_token_bad_signature_falls_through_to_host_channel_resolution_not_a_false_pass()
    {
        var ct = TestContext.Current.CancellationToken;
        var accessor = new TenantContextAccessor();
        var middleware = new TenantContextMiddleware(_ => Task.CompletedTask);
        var service = await ServiceWithVerifiedDomain("portal.example.edu.ng", ct);
        var httpContext = HttpContextForHost("portal.example.edu.ng");
        httpContext.RequestAborted = ct;
        var genuine = SignDemoSessionToken(DemoSecret, ValidDemoClaims());
        var tampered = genuine[..^4] + "0000"; // flip the trailing signature hex
        httpContext.Request.Headers["X-Web-Session-Token"] = tampered;

        await middleware.InvokeAsync(httpContext, accessor, service, NewConfiguredDemoSessionResolver());

        // Falls through to the real, Host-resolved tenant - a tampered marker is treated as
        // absent, never as "confine to synthetic" (which would be a privilege DOWNGRADE used
        // as a denial-of-service lever) and never as a pass-through to something worse.
        Assert.Equal(TenantId, accessor.Context!.TenantId);
        Assert.Equal(new[] { "host" }, accessor.Context.Channels);
    }

    [Fact]
    public async Task An_expired_demo_session_token_falls_through_to_host_channel_resolution()
    {
        var ct = TestContext.Current.CancellationToken;
        var accessor = new TenantContextAccessor();
        var middleware = new TenantContextMiddleware(_ => Task.CompletedTask);
        var service = await ServiceWithVerifiedDomain("portal.example.edu.ng", ct);
        var httpContext = HttpContextForHost("portal.example.edu.ng");
        httpContext.RequestAborted = ct;
        var expiredClaims = ValidDemoClaims(expiresAtMs: DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeMilliseconds());
        httpContext.Request.Headers["X-Web-Session-Token"] = SignDemoSessionToken(DemoSecret, expiredClaims);

        await middleware.InvokeAsync(httpContext, accessor, service, NewConfiguredDemoSessionResolver());

        Assert.Equal(TenantId, accessor.Context!.TenantId);
        Assert.Equal(new[] { "host" }, accessor.Context.Channels);
    }

    [Fact]
    public async Task A_malformed_demo_session_header_value_falls_through_to_host_channel_resolution()
    {
        var ct = TestContext.Current.CancellationToken;
        var accessor = new TenantContextAccessor();
        var middleware = new TenantContextMiddleware(_ => Task.CompletedTask);
        var service = await ServiceWithVerifiedDomain("portal.example.edu.ng", ct);
        var httpContext = HttpContextForHost("portal.example.edu.ng");
        httpContext.RequestAborted = ct;
        httpContext.Request.Headers["X-Web-Session-Token"] = "not-a-real-token-at-all";

        await middleware.InvokeAsync(httpContext, accessor, service, NewConfiguredDemoSessionResolver());

        Assert.Equal(TenantId, accessor.Context!.TenantId);
    }

    [Fact]
    public async Task When_the_resolver_itself_is_unconfigured_a_genuinely_well_formed_token_never_activates_it()
    {
        // Fail-closed by construction: unset WebSessionBridge:TokenSecret/SyntheticTenantId
        // means this resolver can never confine anything, no matter how well-formed the
        // caller's own token looks - the same discipline every secret in this codebase applies.
        var ct = TestContext.Current.CancellationToken;
        var accessor = new TenantContextAccessor();
        var middleware = new TenantContextMiddleware(_ => Task.CompletedTask);
        var service = await ServiceWithVerifiedDomain("portal.example.edu.ng", ct);
        var httpContext = HttpContextForHost("portal.example.edu.ng");
        httpContext.RequestAborted = ct;
        httpContext.Request.Headers["X-Web-Session-Token"] = SignDemoSessionToken(DemoSecret, ValidDemoClaims());

        await middleware.InvokeAsync(httpContext, accessor, service, NewUnconfiguredDemoSessionResolver());

        Assert.Equal(TenantId, accessor.Context!.TenantId);
        Assert.Equal(new[] { "host" }, accessor.Context.Channels);
    }

    [Fact]
    public async Task A_secret_shorter_than_the_minimum_length_never_activates_the_resolver_even_if_a_token_happens_to_verify_against_it()
    {
        const string shortSecret = "too-short";
        var accessor = new TenantContextAccessor();
        var middleware = new TenantContextMiddleware(_ => Task.CompletedTask);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Web-Session-Token"] = SignDemoSessionToken(shortSecret, ValidDemoClaims());

        await middleware.InvokeAsync(httpContext, accessor, NewService(), NewConfiguredDemoSessionResolver(secret: shortSecret));

        Assert.Null(accessor.Context);
    }

    [Fact]
    public void DemoSessionTenantResolver_TryResolve_returns_null_for_a_missing_or_empty_token()
    {
        var resolver = NewConfiguredDemoSessionResolver();
        Assert.Null(resolver.TryResolve(null));
        Assert.Null(resolver.TryResolve(string.Empty));
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Biolog.Api.Audit;
using Biolog.Api.Identifiers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Biolog.IntegrationTests;

/// <summary>
/// End-to-end proof, through the real packaged <c>Biolog.Api</c> host, of BIO-TEN-002's
/// OPS002-F01/OPS002-F02 fixes (BIO-OPS-002 acceptance review, 2026-10-07):
///
/// OPS002-F01 - <c>DemoSessionTenantResolver</c> previously always confined to the ONE
/// configured <c>WebSessionBridge:SyntheticTenantId</c> regardless of the demo session token's
/// own <c>tenantId</c> claim ("alpha" vs "beta"), making the shell's tenant selector cosmetic.
/// These tests prove through this new endpoint's own response (<c>new_tenant_id</c>) that
/// "alpha" and "beta" now resolve to genuinely different configured tenants when both are
/// configured, and (round 2, SY-OPS002-03) that "beta" is REJECTED outright - never silently
/// resolved to alpha's tenant - when only the primary key is configured.
///
/// OPS002-F02 - no durable, actor-attributed audit record existed for a tenant/role context
/// change at all. These tests prove <c>POST /api/v1/tenancy/demo-session/context-change</c>
/// requires a genuinely verified demo session token and records old/new context plus outcome.
///
/// ZB-OPS002-01 (round 4) - <c>previous_actor_id</c> was copied straight from the request body
/// with no validation; the <c>ZB_OPS002_01_*</c> tests below prove it must agree with the
/// value derived from <c>previous_role_id</c> or the request is rejected. The <c>Sink_level_*</c>
/// tests resolve the real <c>InMemoryAuditSink</c> singleton directly (the same pattern
/// <c>DeviceRegistryTests.cs</c> already uses) to assert on the STORED event shape, not just
/// the HTTP response - the 9 original tests only ever checked the latter.
/// </summary>
public sealed class DemoSessionContextAuditEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string DemoSessionSecret = "test-host-e2e-web-session-bridge-secret-context-audit";
    private const string AlphaTenantId = "ten_01a113cb-0d05-774b-a463-a954598ed2e9";
    private const string BetaTenantId = "ten_01a113cb-0d05-774b-a463-a954598ed2f0";
    private const string ContextChangePath = "/api/v1/tenancy/demo-session/context-change";

    private readonly WebApplicationFactory<Program> _factory;

    public DemoSessionContextAuditEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private WebApplicationFactory<Program> WithBetaConfigured() =>
        _factory.WithWebHostBuilder(builder => builder
            .UseEnvironment("Development")
            .ConfigureAppConfiguration(cfg => cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["WebSessionBridge:TokenSecret"] = DemoSessionSecret,
                ["WebSessionBridge:SyntheticTenantId"] = AlphaTenantId,
                ["WebSessionBridge:SyntheticTenantIdBeta"] = BetaTenantId,
            })));

    private WebApplicationFactory<Program> WithOnlyPrimaryConfigured() =>
        _factory.WithWebHostBuilder(builder => builder
            .UseEnvironment("Development")
            .ConfigureAppConfiguration(cfg => cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["WebSessionBridge:TokenSecret"] = DemoSessionSecret,
                ["WebSessionBridge:SyntheticTenantId"] = AlphaTenantId,
            })));

    /// <summary>Signs a demo session token exactly the way web/lib/session-token.ts does - mirrored here, not imported, since this is a cross-stack (TS signer / C# verifier) boundary by design.</summary>
    private static string SignDemoSessionToken(string secret, string roleId = "admin", string tenantId = "alpha")
    {
        var claims = new
        {
            sid = $"sid_{Guid.NewGuid():N}",
            roleId,
            tenantId,
            issuedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            expiresAtMs = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds(),
        };
        var json = JsonSerializer.Serialize(claims);
        var payloadB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        var signature = Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(payloadB64)));
        return $"{payloadB64}.{signature}";
    }

    private static Task<HttpResponseMessage> PostContextChangeAsync(HttpClient client, string? token, object? body, CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, ContextChangePath)
        {
            Content = JsonContent.Create(body ?? new { }),
        };
        if (token is not null)
        {
            request.Headers.Add("X-Web-Session-Token", token);
        }
        return client.SendAsync(request, ct);
    }

    [Fact]
    public async Task No_token_at_all_is_rejected_as_an_invalid_demo_session()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = WithBetaConfigured().CreateClient();

        var response = await PostContextChangeAsync(client, token: null, body: null, ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("demo_session_invalid", body.GetProperty("title").GetString());
    }

    [Fact]
    public async Task A_tampered_token_is_rejected_as_an_invalid_demo_session()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = WithBetaConfigured().CreateClient();
        var genuine = SignDemoSessionToken(DemoSessionSecret);
        var tampered = genuine[..^4] + "0000";

        var response = await PostContextChangeAsync(client, tampered, body: null, ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task OPS002_F01_alpha_and_beta_resolve_to_genuinely_different_configured_tenants()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = WithBetaConfigured().CreateClient();

        var alphaResponse = await PostContextChangeAsync(client, SignDemoSessionToken(DemoSessionSecret, tenantId: "alpha"), body: null, ct);
        var betaResponse = await PostContextChangeAsync(client, SignDemoSessionToken(DemoSessionSecret, tenantId: "beta"), body: null, ct);

        Assert.Equal(HttpStatusCode.OK, alphaResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, betaResponse.StatusCode);
        var alphaBody = await alphaResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        var betaBody = await betaResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        var alphaTenantId = alphaBody.GetProperty("data").GetProperty("new_tenant_id").GetString();
        var betaTenantId = betaBody.GetProperty("data").GetProperty("new_tenant_id").GetString();

        Assert.Equal(AlphaTenantId, alphaTenantId);
        Assert.Equal(BetaTenantId, betaTenantId);
        Assert.NotEqual(alphaTenantId, betaTenantId);
    }

    [Fact]
    public async Task SY_OPS002_03_beta_is_rejected_outright_never_silently_resolved_to_alpha_when_the_second_key_is_unconfigured()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = WithOnlyPrimaryConfigured().CreateClient();

        var alphaResponse = await PostContextChangeAsync(client, SignDemoSessionToken(DemoSessionSecret, tenantId: "alpha"), body: null, ct);
        var betaResponse = await PostContextChangeAsync(client, SignDemoSessionToken(DemoSessionSecret, tenantId: "beta"), body: null, ct);

        var alphaBody = await alphaResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal(HttpStatusCode.OK, alphaResponse.StatusCode);
        Assert.Equal(AlphaTenantId, alphaBody.GetProperty("data").GetProperty("new_tenant_id").GetString());

        Assert.Equal(HttpStatusCode.Unauthorized, betaResponse.StatusCode);
        var betaBody = await betaResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("demo_session_invalid", betaBody.GetProperty("title").GetString());
    }

    [Fact]
    public async Task OPS002_F02_a_genuinely_verified_switch_with_no_previous_context_is_recorded_as_an_initial_session()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = WithBetaConfigured().CreateClient();

        var response = await PostContextChangeAsync(
            client,
            SignDemoSessionToken(DemoSessionSecret, roleId: "staff", tenantId: "alpha"),
            body: new { previous_tenant_id = (string?)null, previous_role_id = (string?)null, previous_actor_id = (string?)null },
            ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        var data = body.GetProperty("data");
        Assert.Equal(AlphaTenantId, data.GetProperty("new_tenant_id").GetString());
        Assert.Equal("staff", data.GetProperty("new_role_id").GetString());
        Assert.False(string.IsNullOrEmpty(data.GetProperty("event_id").GetString()));
    }

    [Fact]
    public async Task OPS002_F02_a_genuine_switch_with_a_full_previous_context_is_accepted()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = WithBetaConfigured().CreateClient();

        var response = await PostContextChangeAsync(
            client,
            SignDemoSessionToken(DemoSessionSecret, roleId: "admin", tenantId: "beta"),
            body: new { previous_tenant_id = "alpha", previous_role_id = "viewer", previous_actor_id = "per_01a113f6-6268-757e-926c-4a1b5f98b45b" },
            ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_partially_provided_previous_context_is_rejected()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = WithBetaConfigured().CreateClient();

        var response = await PostContextChangeAsync(
            client,
            SignDemoSessionToken(DemoSessionSecret),
            body: new { previous_tenant_id = "alpha", previous_role_id = (string?)null, previous_actor_id = (string?)null },
            ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("incomplete_previous_context", body.GetProperty("title").GetString());
    }

    [Fact]
    public async Task An_unrecognised_previous_tenant_id_is_rejected()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = WithBetaConfigured().CreateClient();

        var response = await PostContextChangeAsync(
            client,
            SignDemoSessionToken(DemoSessionSecret),
            body: new { previous_tenant_id = "gamma", previous_role_id = "viewer", previous_actor_id = "per_01a113f6-6268-757e-926c-4a1b5f98b45b" },
            ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("invalid_previous_tenant_id", body.GetProperty("title").GetString());
    }

    [Fact]
    public async Task An_unrecognised_previous_role_id_is_rejected()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = WithBetaConfigured().CreateClient();

        var response = await PostContextChangeAsync(
            client,
            SignDemoSessionToken(DemoSessionSecret),
            body: new { previous_tenant_id = "alpha", previous_role_id = "superuser", previous_actor_id = "per_01a113f6-6268-757e-926c-4a1b5f98b45b" },
            ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("invalid_previous_role_id", body.GetProperty("title").GetString());
    }

    [Fact]
    public async Task ZB_OPS002_01_a_previous_actor_id_that_disagrees_with_the_derived_value_for_previous_role_id_is_rejected()
    {
        // The actual ZB-OPS002-01 exploit shape: previous_role_id is a VALID, known role, but
        // previous_actor_id is some other string entirely (here: a different role's own real
        // actor id, but it could just as easily be arbitrary free text) - must be rejected, not
        // silently stored.
        var ct = TestContext.Current.CancellationToken;
        var client = WithBetaConfigured().CreateClient();

        var response = await PostContextChangeAsync(
            client,
            SignDemoSessionToken(DemoSessionSecret),
            body: new { previous_tenant_id = "alpha", previous_role_id = "viewer", previous_actor_id = "per_01a113f6-6268-730c-a5ce-44d97f0aa7f5" /* admin's id, not viewer's */ },
            ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("previous_actor_id_role_mismatch", body.GetProperty("title").GetString());
    }

    [Fact]
    public async Task ZB_OPS002_01_an_arbitrary_free_text_previous_actor_id_is_rejected_not_persisted()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = WithBetaConfigured().CreateClient();

        var response = await PostContextChangeAsync(
            client,
            SignDemoSessionToken(DemoSessionSecret),
            body: new { previous_tenant_id = "alpha", previous_role_id = "viewer", previous_actor_id = "<script>alert(1)</script> not a person id at all" },
            ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("previous_actor_id_role_mismatch", body.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Sink_level_the_stored_event_has_exactly_the_expected_Detail_keys_null_Device_and_internal_classification()
    {
        var ct = TestContext.Current.CancellationToken;
        var factory = WithBetaConfigured();
        var client = factory.CreateClient();

        var response = await PostContextChangeAsync(
            client,
            SignDemoSessionToken(DemoSessionSecret, roleId: "admin", tenantId: "beta"),
            body: new { previous_tenant_id = "alpha", previous_role_id = "viewer", previous_actor_id = "per_01a113f6-6268-757e-926c-4a1b5f98b45b" },
            ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var eventId = (await response.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("data").GetProperty("event_id").GetString();

        var sink = factory.Services.GetRequiredService<InMemoryAuditSink>();
        var recorded = Assert.Single(sink.Events, e => e.EventId == eventId);

        Assert.Null(recorded.Device);
        Assert.Equal("internal", recorded.Classification);
        Assert.Equal(AuditOutcome.Allowed, recorded.Outcome);
        Assert.Equal(BetaTenantId, recorded.TenantId);
        Assert.Equal(
            new HashSet<string> { "previous_tenant_id", "previous_role_id", "previous_actor_id", "new_role_id" },
            recorded.Detail.Keys.ToHashSet());
        Assert.Equal("alpha", recorded.Detail["previous_tenant_id"]);
        Assert.Equal("viewer", recorded.Detail["previous_role_id"]);
        // ZB-OPS002-01: the DERIVED value, not merely equal to what the caller sent - proven by
        // this being the same literal viewer actor id regardless of what request shaped it.
        Assert.Equal("per_01a113f6-6268-757e-926c-4a1b5f98b45b", recorded.Detail["previous_actor_id"]);
        Assert.Equal("admin", recorded.Detail["new_role_id"]);
    }

    [Fact]
    public async Task Sink_level_a_well_formed_evt_correlation_id_is_preserved_but_a_malformed_one_is_discarded_and_replaced()
    {
        var ct = TestContext.Current.CancellationToken;
        var factory = WithBetaConfigured();
        var client = factory.CreateClient();
        var sink = factory.Services.GetRequiredService<InMemoryAuditSink>();

        var wellFormedCorrelationId = CanonicalId.NewEventId();
        var wellFormedRequest = new HttpRequestMessage(HttpMethod.Post, ContextChangePath)
        {
            Content = JsonContent.Create(new { }),
        };
        wellFormedRequest.Headers.Add("X-Web-Session-Token", SignDemoSessionToken(DemoSessionSecret));
        wellFormedRequest.Headers.Add("X-Correlation-Id", wellFormedCorrelationId);
        var wellFormedResponse = await client.SendAsync(wellFormedRequest, ct);
        var wellFormedEventId = (await wellFormedResponse.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("data").GetProperty("event_id").GetString();
        var wellFormedRecorded = Assert.Single(sink.Events, e => e.EventId == wellFormedEventId);
        Assert.Equal(wellFormedCorrelationId, wellFormedRecorded.CorrelationId);

        var malformedRequest = new HttpRequestMessage(HttpMethod.Post, ContextChangePath)
        {
            Content = JsonContent.Create(new { }),
        };
        malformedRequest.Headers.Add("X-Web-Session-Token", SignDemoSessionToken(DemoSessionSecret));
        malformedRequest.Headers.Add("X-Correlation-Id", "not-a-canonical-event-id-at-all");
        var malformedResponse = await client.SendAsync(malformedRequest, ct);
        var malformedEventId = (await malformedResponse.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("data").GetProperty("event_id").GetString();
        var malformedRecorded = Assert.Single(sink.Events, e => e.EventId == malformedEventId);
        Assert.NotEqual("not-a-canonical-event-id-at-all", malformedRecorded.CorrelationId);
        Assert.True(CanonicalId.IsWellFormed(malformedRecorded.CorrelationId, "evt"));
    }
}

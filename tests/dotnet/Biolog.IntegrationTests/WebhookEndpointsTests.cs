using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Biolog.Api.Tenancy;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Biolog.IntegrationTests;

/// <summary>
/// Developer tests for BIO-INT-004, exercised through the real composed host (the real
/// <see cref="Api.Modules.BioInt004Module"/> DI wiring). Uses the same header-switched tenant
/// test seam <see cref="AuthenticationEndpointsTests"/> established, and the same
/// real-provenance-secret-via-<c>ConfigureAppConfiguration</c> seam every sibling integration
/// test file uses.
///
/// <b>Same disclosed limitation as <see cref="ApiSecurityEndpointsTests"/> (ZB-INT003-02).</b>
/// This project has no <c>InternalsVisibleTo</c> access to seed a real permission grant into
/// BIO-IDN-006's internals, and every one of this story's own endpoints requires a permission
/// (unlike BIO-INT-003's OAuth token/whoami routes, which need none) - so every create/list/
/// toggle call here can only demonstrate reaching the REAL permission check and being rejected
/// for lack of authority, never a mutation succeeding via HTTP. The underlying AC01 positive-
/// path proofs (tenant isolation, a fresh random signing secret per subscription, enabling/
/// disabling, concurrency conflicts), with a permission gateway that grants, live at the unit
/// level in <c>WebhookSubscriptionServiceTests.cs</c>; AC02/AC03/AC04's own positive paths
/// (versioned wire payload, signing, retry/dedup/dead-letter) live in
/// <c>WebhookDispatchServiceTests.cs</c>/<c>WebhookSignerTests.cs</c>. What IS provable
/// end-to-end here without a grant: request-shape validation that runs BEFORE the permission
/// check in the create handler (invalid URL, unknown event type) - these prove the real HTTP
/// wiring without needing authority to actually create anything.
/// </summary>
public sealed class WebhookEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private const string TestTenantHeader = "X-Test-Tenant-Id";
    private const string ProvenanceSecret = "test-host-e2e-actor-provenance-secret-bio-int-004";
    private const string TestActorId = "per_test_unprivileged_actor";

    public WebhookEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder
            .UseEnvironment("Development")
            .ConfigureAppConfiguration(cfg => cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Webhooks:ActorProvenanceSecret"] = ProvenanceSecret,
            })));
    }

    private static string ComputeProvenanceProof(string actorId) =>
        Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(ProvenanceSecret), Encoding.UTF8.GetBytes(actorId)));

    private WebApplicationFactory<Program> WithTenantSwitching() =>
        _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ITenantContextAccessor>();
            services.AddHttpContextAccessor();
            services.AddScoped<ITenantContextAccessor, HeaderSwitchedTenantContextAccessor>();
        }));

    private sealed class HeaderSwitchedTenantContextAccessor(IHttpContextAccessor httpContextAccessor) : ITenantContextAccessor
    {
        public TenantContext? Context
        {
            get
            {
                var tenantId = httpContextAccessor.HttpContext?.Request.Headers[TestTenantHeader].FirstOrDefault();
                return tenantId is null ? null : new TenantContext(tenantId, $"binding-{tenantId}", []);
            }
            set { /* ignore - resolved from the test header instead */ }
        }
    }

    private static HttpClient ClientForTenant(WebApplicationFactory<Program> factory, string tenantId)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestTenantHeader, tenantId);
        return client;
    }

    private static HttpClient WithValidActor(HttpClient client)
    {
        client.DefaultRequestHeaders.Add("X-Actor-Id", TestActorId);
        client.DefaultRequestHeaders.Add("X-Actor-Provenance", ComputeProvenanceProof(TestActorId));
        return client;
    }

    private static async Task<string> ProblemTitleAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        return body.GetProperty("title").GetString() ?? string.Empty;
    }

    [Fact]
    public async Task Create_with_no_actor_id_at_all_is_rejected_before_anything_else()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = ClientForTenant(WithTenantSwitching(), "ten_webhooks_ac01");

        var response = await client.PostAsJsonAsync("/api/v1/integrations/webhooks/", new { url = "https://example-synthetic.test/hook", eventTypes = new[] { "device" } }, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("actor_required", await ProblemTitleAsync(response, ct));
    }

    [Fact]
    public async Task Create_with_an_actor_id_but_no_provenance_proof_is_rejected()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = ClientForTenant(WithTenantSwitching(), "ten_webhooks_ac01");
        client.DefaultRequestHeaders.Add("X-Actor-Id", TestActorId);

        var response = await client.PostAsJsonAsync("/api/v1/integrations/webhooks/", new { url = "https://example-synthetic.test/hook", eventTypes = new[] { "device" } }, ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("actor_provenance_required", await ProblemTitleAsync(response, ct));
    }

    [Fact]
    public async Task Create_with_no_resolved_tenant_is_rejected()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = WithValidActor(_factory.CreateClient()); // no WithTenantSwitching() -> no tenant header consumer registered -> real host-channel path, no Host match

        var response = await client.PostAsJsonAsync("/api/v1/integrations/webhooks/", new { url = "https://example-synthetic.test/hook", eventTypes = new[] { "device" } }, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("tenant_context_missing", await ProblemTitleAsync(response, ct));
    }

    [Fact]
    public async Task Create_rejects_a_non_absolute_or_non_http_url_before_any_permission_check()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = WithValidActor(ClientForTenant(WithTenantSwitching(), "ten_webhooks_ac01"));

        var response = await client.PostAsJsonAsync("/api/v1/integrations/webhooks/", new { url = "not-a-url", eventTypes = new[] { "device" } }, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_url", await ProblemTitleAsync(response, ct));
    }

    [Fact]
    public async Task Create_rejects_an_unknown_event_type_before_any_permission_check()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = WithValidActor(ClientForTenant(WithTenantSwitching(), "ten_webhooks_ac01"));

        var response = await client.PostAsJsonAsync(
            "/api/v1/integrations/webhooks/", new { url = "https://example-synthetic.test/hook", eventTypes = new[] { "not_a_real_event_type" } }, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("unknown_event_type", await ProblemTitleAsync(response, ct));
    }

    [Fact]
    public async Task A_genuinely_authenticated_create_request_with_no_grant_reaches_the_real_permission_check_never_a_bare_success()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = WithValidActor(ClientForTenant(WithTenantSwitching(), "ten_webhooks_ac01"));

        var response = await client.PostAsJsonAsync("/api/v1/integrations/webhooks/", new { url = "https://example-synthetic.test/hook", eventTypes = new[] { "device" } }, ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("permission_required", await ProblemTitleAsync(response, ct));
    }

    [Fact]
    public async Task A_genuinely_authenticated_list_request_with_no_grant_reaches_the_real_permission_check()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = WithValidActor(ClientForTenant(WithTenantSwitching(), "ten_webhooks_ac01"));

        var response = await client.GetAsync("/api/v1/integrations/webhooks/", ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("permission_required", await ProblemTitleAsync(response, ct));
    }

    [Fact]
    public async Task A_genuinely_authenticated_status_toggle_request_with_no_grant_reaches_the_real_permission_check()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = WithValidActor(ClientForTenant(WithTenantSwitching(), "ten_webhooks_ac01"));

        var response = await client.PutAsJsonAsync("/api/v1/integrations/webhooks/whs_does-not-matter/status", new { enabled = false, version = 1 }, ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("permission_required", await ProblemTitleAsync(response, ct));
    }

    [Fact]
    public async Task List_with_no_resolved_tenant_is_rejected_before_any_permission_check()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = WithValidActor(_factory.CreateClient());

        var response = await client.GetAsync("/api/v1/integrations/webhooks/", ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("tenant_context_missing", await ProblemTitleAsync(response, ct));
    }
}

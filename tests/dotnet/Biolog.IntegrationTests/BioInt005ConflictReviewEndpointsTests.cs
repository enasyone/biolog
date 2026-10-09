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
/// Developer tests for BIO-INT-005, exercised through the real composed host (the real
/// <see cref="Api.Modules.BioInt005Module"/> DI wiring). Uses the same header-switched tenant
/// test seam <see cref="AuthenticationEndpointsTests"/>/<see cref="WebhookEndpointsTests"/>
/// already established, and the same real-provenance-secret-via-<c>ConfigureAppConfiguration</c>
/// seam every sibling integration test file uses.
///
/// <b>Same disclosed limitation as <see cref="WebhookEndpointsTests"/> (ZB-INT003-02).</b> This
/// project has no <c>InternalsVisibleTo</c> access to seed a real permission grant into
/// BIO-IDN-006's internals, and EVERY route here now requires a permission
/// (ZB-INT005-01/ZB-INT005-02 round-1 remediation - scan/list were originally exempted by an
/// incorrect ADR-0006 Decision 2 analogy, see <c>ImportConflictReviewEndpoints</c>'s own header
/// for the full correction) - so every call here can only demonstrate reaching the REAL
/// permission check and being rejected for lack of authority, never a mutation or a read
/// succeeding via HTTP. The underlying AC01/AC02/AC03 positive paths (scan/list/decide with a
/// permission gateway that grants) live at the unit level in
/// <c>ImportConflictReviewServiceTests.cs</c>. What IS provable end-to-end here without a grant:
/// request-shape/actor/provenance validation that runs BEFORE the permission check in every
/// handler, and that the permission check itself is genuinely reached and genuinely denies.
/// </summary>
public sealed class BioInt005ConflictReviewEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private const string TestTenantHeader = "X-Test-Tenant-Id";
    private const string ProvenanceSecret = "test-host-e2e-actor-provenance-secret-bio-int-005";
    private const string TestActorId = "per_test_unprivileged_actor";

    public BioInt005ConflictReviewEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder
            .UseEnvironment("Development")
            .ConfigureAppConfiguration(cfg => cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Integrations:ActorProvenanceSecret"] = ProvenanceSecret,
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

    private static async Task<string> ProblemCodeAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        return body.GetProperty("code").GetString() ?? string.Empty;
    }

    [Fact]
    public async Task Scan_with_no_actor_id_at_all_is_rejected_before_anything_else()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = ClientForTenant(WithTenantSwitching(), "ten_int005_ac01");

        var response = await client.PostAsync("/api/v1/integrations/mappings/map_does-not-matter/conflicts/scan", null, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("actor_required", await ProblemCodeAsync(response, ct));
    }

    [Fact]
    public async Task Scan_with_no_resolved_tenant_is_rejected()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = WithValidActor(_factory.CreateClient()); // no WithTenantSwitching() -> real host-channel path, no Host match

        var response = await client.PostAsync("/api/v1/integrations/mappings/map_does-not-matter/conflicts/scan", null, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("tenant_context_missing", await ProblemCodeAsync(response, ct));
    }

    [Fact]
    public async Task Scan_with_an_actor_id_but_no_provenance_proof_is_rejected()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = ClientForTenant(WithTenantSwitching(), "ten_int005_ac01");
        client.DefaultRequestHeaders.Add("X-Actor-Id", TestActorId);

        var response = await client.PostAsync("/api/v1/integrations/mappings/map_does-not-matter/conflicts/scan", null, ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("actor_provenance_required", await ProblemCodeAsync(response, ct));
    }

    [Fact]
    public async Task ZB_INT005_01_a_genuinely_authenticated_and_provenance_verified_scan_request_with_no_grant_reaches_the_real_permission_check_never_a_bare_success()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = WithValidActor(ClientForTenant(WithTenantSwitching(), "ten_int005_ac01"));

        var response = await client.PostAsync("/api/v1/integrations/mappings/map_does-not-matter/conflicts/scan", null, ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("permission_required", await ProblemCodeAsync(response, ct));
    }

    [Fact]
    public async Task List_with_an_actor_id_but_no_provenance_proof_is_rejected()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = ClientForTenant(WithTenantSwitching(), "ten_int005_ac01");
        client.DefaultRequestHeaders.Add("X-Actor-Id", TestActorId);

        var response = await client.GetAsync("/api/v1/integrations/mappings/map_does-not-matter/conflicts", ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("actor_provenance_required", await ProblemCodeAsync(response, ct));
    }

    [Fact]
    public async Task ZB_INT005_02_a_genuinely_authenticated_and_provenance_verified_list_request_with_no_grant_reaches_the_real_permission_check_never_exposes_data()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = WithValidActor(ClientForTenant(WithTenantSwitching(), "ten_int005_ac01"));

        var response = await client.GetAsync("/api/v1/integrations/mappings/map_does-not-matter/conflicts", ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("permission_required", await ProblemCodeAsync(response, ct));
    }

    [Fact]
    public async Task List_with_no_resolved_tenant_is_rejected()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = WithValidActor(_factory.CreateClient());

        var response = await client.GetAsync("/api/v1/integrations/mappings/map_does-not-matter/conflicts", ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("tenant_context_missing", await ProblemCodeAsync(response, ct));
    }

    [Fact]
    public async Task Decide_with_no_actor_id_at_all_is_rejected_before_anything_else()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = ClientForTenant(WithTenantSwitching(), "ten_int005_ac02");

        var response = await client.PostAsJsonAsync(
            "/api/v1/integrations/mappings/map_does-not-matter/conflicts/ext-1/decisions",
            new { decision = "KEEP_EXISTING", reason_code = "ops_review", version = 1 }, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("actor_required", await ProblemCodeAsync(response, ct));
    }

    [Fact]
    public async Task Decide_with_an_actor_id_but_no_provenance_proof_is_rejected()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = ClientForTenant(WithTenantSwitching(), "ten_int005_ac02");
        client.DefaultRequestHeaders.Add("X-Actor-Id", TestActorId);

        var response = await client.PostAsJsonAsync(
            "/api/v1/integrations/mappings/map_does-not-matter/conflicts/ext-1/decisions",
            new { decision = "KEEP_EXISTING", reason_code = "ops_review", version = 1 }, ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("actor_provenance_required", await ProblemCodeAsync(response, ct));
    }

    [Fact]
    public async Task A_genuinely_authenticated_and_provenance_verified_decide_request_with_no_grant_reaches_the_real_permission_check_never_a_bare_success()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = WithValidActor(ClientForTenant(WithTenantSwitching(), "ten_int005_ac02"));

        var response = await client.PostAsJsonAsync(
            "/api/v1/integrations/mappings/map_does-not-matter/conflicts/ext-1/decisions",
            new { decision = "KEEP_EXISTING", reason_code = "ops_review", version = 1 }, ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("permission_required", await ProblemCodeAsync(response, ct));
    }

    [Fact]
    public async Task Decide_with_no_resolved_tenant_is_rejected_before_the_permission_check()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = WithValidActor(_factory.CreateClient());

        var response = await client.PostAsJsonAsync(
            "/api/v1/integrations/mappings/map_does-not-matter/conflicts/ext-1/decisions",
            new { decision = "KEEP_EXISTING", reason_code = "ops_review", version = 1 }, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("tenant_context_missing", await ProblemCodeAsync(response, ct));
    }
}

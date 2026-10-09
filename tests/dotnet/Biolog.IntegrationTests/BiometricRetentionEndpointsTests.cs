using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Biolog.Api.Tenancy;
using Biolog.Features.BiometricRetention.Api;
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
/// Developer tests for BIO-BIO-009-AC01/AC02/AC03/AC04: exercised through the real composed host
/// and the real <see cref="Api.Modules.BioBio009Module"/> DI wiring. Uses the same header-switched
/// tenant test seam <c>LivenessEndpointsTests</c>/<c>NotificationEndpointsTests</c> already
/// established.
///
/// <b>Same disclosed limitation as BIO-ACC-001's own <c>AccessPolicyHostChannelTests.cs</c>.</b>
/// This project has no way to seed a real BIO-IDN-006 permission grant through a public endpoint
/// alone (a tenant's very first SUPER_ADMIN cannot be created through any public endpoint at all
/// - BIO-IDN-006-QA-01's own disclosed consequence). So this file proves the real provenance/RBAC
/// gate correctly REJECTS an unprivileged or unproven caller through the real host, never a
/// mutation SUCCEEDING here. The underlying AC01/AC02/AC03/AC04 positive-path business-logic
/// proofs (policy set/read, raw-image authorisation, deletion outcomes, gallery propagation) live
/// at the unit level in <c>RetentionPolicyServiceTests.cs</c>/<c>BiometricDeletionServiceTests.cs</c>,
/// which exercise the services directly - those services take no permission parameter at all
/// (authorization is entirely an endpoint-layer concern, see <c>BioBio009Module</c>'s own header).
/// </summary>
public sealed class BiometricRetentionEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private const string TestTenantHeader = "X-Test-Tenant-Id";
    private const string PolicyEndpoint = "/api/v1/biometrics/retention/policy";
    private const string RawImageEndpoint = "/api/v1/biometrics/retention/raw-image-authorization";
    private const string DeleteEndpoint = "/api/v1/biometrics/retention/delete";
    private const string ProvenanceSecret = "real-host-e2e-provenance-secret-bio009";

    public BiometricRetentionEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder
            .UseEnvironment("Development")
            .ConfigureAppConfiguration(cfg => cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BiometricRetention:ActorProvenanceSecret"] = ProvenanceSecret,
            }))
            .ConfigureTestServices(services =>
            {
                services.RemoveAll<ITenantContextAccessor>();
                services.AddHttpContextAccessor();
                services.AddScoped<ITenantContextAccessor, HeaderSwitchedTenantContextAccessor>();
            }));
    }

    private static string ComputeProvenanceProof(string actorId) =>
        Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(ProvenanceSecret), Encoding.UTF8.GetBytes(actorId)));

    private static HttpClient CreateClientForTenant(WebApplicationFactory<Program> factory, string tenantId)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestTenantHeader, tenantId);
        return client;
    }

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

    [Fact]
    public async Task AC01_an_unconfigured_tenant_reports_the_documented_default()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = CreateClientForTenant(_factory, "ten_bio009_default");

        var response = await client.GetAsync($"{PolicyEndpoint}?category=face_template", ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal(90, body.GetProperty("retentionDays").GetInt32());
        Assert.False(body.GetProperty("isExplicitlyConfigured").GetBoolean());
    }

    [Fact]
    public async Task AC03_a_tenant_that_has_never_authorised_raw_image_retention_defaults_to_off()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = CreateClientForTenant(_factory, "ten_bio009_rawdefault");

        var response = await client.GetAsync(RawImageEndpoint, ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.False(body.GetProperty("authorised").GetBoolean());
    }

    /// <summary>ADR-0006 Decision 3: a request asserting an actor id but presenting no
    /// <c>X-Actor-Provenance</c> proof at all is rejected BEFORE any permission lookup is even
    /// attempted - the same shape BIO-ACC-001's own equivalent test proves for its own
    /// endpoints.</summary>
    [Theory]
    [InlineData(PolicyEndpoint)]
    [InlineData(RawImageEndpoint)]
    [InlineData(DeleteEndpoint)]
    public async Task ZA_BIO009_01_a_caller_without_any_provenance_proof_is_rejected_before_authority_is_checked(string endpoint)
    {
        var ct = TestContext.Current.CancellationToken;
        var client = CreateClientForTenant(_factory, "ten_bio009_noproof");
        client.DefaultRequestHeaders.Add("X-Actor-Id", "unprivileged_attacker"); // deliberately no X-Actor-Provenance at all

        var response = await client.PostAsJsonAsync(endpoint, new { }, ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("actor_provenance_required", body.GetProperty("title").GetString());
    }

    /// <summary>A valid provenance proof alone is not authority - without a real BIO-IDN-006
    /// grant, the real <c>PermissionService.ActorHasPermissionAsync</c> correctly reports "not
    /// granted" for an actor id that is not even a well-formed, existing person - proving this
    /// gateway reaches the REAL RBAC store through the real host, not a stub.</summary>
    [Theory]
    [InlineData(PolicyEndpoint)]
    [InlineData(RawImageEndpoint)]
    [InlineData(DeleteEndpoint)]
    public async Task ZA_BIO009_01_a_valid_provenance_proof_without_a_real_grant_is_still_forbidden(string endpoint)
    {
        var ct = TestContext.Current.CancellationToken;
        var client = CreateClientForTenant(_factory, "ten_bio009_noauthority");
        const string actorId = "unprivileged_attacker";
        client.DefaultRequestHeaders.Add("X-Actor-Id", actorId);
        client.DefaultRequestHeaders.Add("X-Actor-Provenance", ComputeProvenanceProof(actorId));

        var response = await client.PostAsJsonAsync(endpoint, new { }, ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("permission_required", body.GetProperty("title").GetString());
    }

    /// <summary>ZB-BIO009-05 (round 3, floor-wide bug): before this fix,
    /// <c>HmacActorProvenanceStore</c> fell back to a key derived from a public, hardcoded
    /// literal when unconfigured - anyone reading the source could compute the same HMAC and
    /// forge a proof for any actor id. Proves the fix: with the secret genuinely unconfigured,
    /// even a proof computed with that FORMER public fallback key is rejected, never accepted.</summary>
    [Fact]
    public async Task ZB_BIO009_05_an_unconfigured_secret_rejects_even_the_former_public_dummy_key_proof()
    {
        var ct = TestContext.Current.CancellationToken;
        using var factoryWithoutSecret = _factory.WithWebHostBuilder(builder => builder
            .ConfigureAppConfiguration(cfg => cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                [ActorProvenanceProvisioning.ConfigurationKey] = null,
            })));
        var client = CreateClientForTenant(factoryWithoutSecret, "ten_bio009_dummykey");
        const string actorId = "formerly_privileged_actor";
        var formerDummyKey = SHA256.HashData(Encoding.UTF8.GetBytes("\0no-provenance-secret-configured\0"));
        var forgedProof = Convert.ToHexString(HMACSHA256.HashData(formerDummyKey, Encoding.UTF8.GetBytes(actorId)));
        client.DefaultRequestHeaders.Add("X-Actor-Id", actorId);
        client.DefaultRequestHeaders.Add("X-Actor-Provenance", forgedProof);

        var response = await client.PostAsJsonAsync(PolicyEndpoint, new { }, ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("actor_provenance_required", body.GetProperty("title").GetString());
    }

    [Fact]
    public async Task fails_closed_with_no_resolved_tenant_context()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"{PolicyEndpoint}?category=face_template", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}

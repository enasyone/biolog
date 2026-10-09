using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Biolog.IntegrationTests;

/// <summary>
/// TD-WEB-PROVENANCE-BRIDGE-REVERIFY/BIO-TEN-003 (Tunde, 2026-10-07): end-to-end proof, through
/// the REAL packaged <c>Biolog.Api</c> host, that a signed web-session identity reaches a
/// genuine authorized "granted" result for THIS story's own endpoint - not just
/// <c>permission_required</c>, the disclosed ceiling <see cref="DemoSessionTenantResolverEndpointsTests"/>'s
/// own header names for its chosen target endpoint ("no test project has
/// <c>InternalsVisibleTo</c> access to seed a real permission grant anywhere in this codebase").
///
/// That limitation does not apply here: <c>Biolog.Api.Modules.BioTen003Module.Configure</c>
/// seeds the synthetic tenant (BIO-TEN-001's own <c>ITenantRegistry</c>) AND the real
/// <c>RolePermissionGrant</c> rows (BIO-IDN-006's own <c>IRolePermissionGrantRepository</c>)
/// itself, at HOST STARTUP, whenever <c>WebSessionBridge:SyntheticTenantId</c> is configured -
/// exactly the same real host pipeline every other story's own module uses, not a test-only
/// seam. So this test can - and does - reach a genuine 200 on both the read and the write, not
/// merely a "resolution succeeded, permission check reached" proof.
///
/// Uses the real <see cref="Biolog.Api.Tenancy.DemoSessionIdentitySeed"/> person ids directly
/// (<c>per_&lt;UUIDv7&gt;</c> literals, round 6) rather than going through a signed
/// <c>X-Web-Session-Token</c>/proxy round-trip - this test proves the BACKEND half (seeding +
/// RBAC + this story's own endpoint), the same scope boundary
/// <see cref="DemoSessionTenantResolverEndpointsTests"/>/<see cref="DemoSessionIdentitySeedEndpointsTests"/>
/// both already draw. The browser/proxy half (session issuance, actor-id derivation, HMAC
/// injection) is proven separately by `tests/frontend/playwright/
/// bio-ten-003-feature-configuration-live.spec.ts` (opt-in, two real servers,
/// `BIOLOG_TEN003_LIVE_TESTS=1`) - see that file and docs/features/bio-ten-003/README.md's own
/// "Post-acceptance fix: TD-WEB-PROVENANCE-BRIDGE-REVERIFY" section for why both exist rather
/// than relying on just one.
/// </summary>
public sealed class BioTen003ReverifyEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private const string ProvenanceSecret = "test-host-e2e-ten003-actor-provenance-secret-bridge";
    private const string DemoSessionSecret = "test-host-e2e-ten003-web-session-bridge-secret-demo";
    private const string SyntheticTenantId = "ten_01a113cb-0d05-774b-a463-a954598ed2e9";

    private const string AdminPersonId = "per_01a113f6-6268-730c-a5ce-44d97f0aa7f5";
    private const string ViewerPersonId = "per_01a113f6-6268-757e-926c-4a1b5f98b45b";

    public BioTen003ReverifyEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder
            .UseEnvironment("Development")
            .ConfigureAppConfiguration(cfg => cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TenantFeatureConfig:ActorProvenanceSecret"] = ProvenanceSecret,
                ["WebSessionBridge:TokenSecret"] = DemoSessionSecret,
                ["WebSessionBridge:SyntheticTenantId"] = SyntheticTenantId,
            })));
    }

    private static string ComputeProvenanceProof(string actorId) =>
        Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(ProvenanceSecret), Encoding.UTF8.GetBytes(actorId)));

    /// <summary>
    /// Signs a demo session token exactly the way web/lib/session-token.ts does (mirrored, not
    /// imported - a cross-stack TS-signer/C#-verifier boundary by design, same as
    /// <see cref="DemoSessionTenantResolverEndpointsTests"/>'s own copy).
    ///
    /// Genuinely required here, found while writing this test: this story's own endpoint takes
    /// its target tenant id EXPLICITLY from the URL (never ambient), but
    /// <see cref="Biolog.Features.Rbac.PermissionService.ActorHasPermissionAsync"/>'s own
    /// person-existence check (<c>IPersonExistenceGateway</c> -&gt;
    /// <c>Biolog.Features.PersonRegistry.PersonService.GetAsync</c>) resolves its tenant
    /// AMBIENTLY (<c>RequireTenant()</c>) regardless of the explicit <c>tenantId</c> parameter
    /// <see cref="Biolog.Features.TenantFeatureConfig.Api.TenantFeatureConfigurationEndpoints"/>
    /// passes it - a pre-existing BIO-IDN-006 design property, not something this round
    /// introduces or fixes. With no ambient tenant resolved at all (a bare <see cref="WebApplicationFactory{TEntryPoint}"/>
    /// has no registered Host-based tenant domain), that existence check throws
    /// <c>PersonError.TenantContextMissing</c> - an exception type
    /// <see cref="Biolog.Features.TenantFeatureConfig.Api.TenantFeatureConfigurationEndpoints"/>'s
    /// own try/catch never catches (it only catches its own
    /// <see cref="Biolog.Features.TenantFeatureConfig.Domain.TenantFeatureConfigurationError"/>),
    /// surfacing as an unhandled 500 rather than any of this test's own intended assertions.
    /// A genuinely signed session token routes through round 5's
    /// <c>DemoSessionTenantResolver</c>, which resolves ambient tenant to the same synthetic
    /// tenant id BEFORE host-channel resolution runs - satisfying <c>RequireTenant()</c> without
    /// this story's own endpoint ever consulting ambient tenant context itself.
    /// </summary>
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

    private HttpClient ClientAs(string personId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Actor-Id", personId);
        client.DefaultRequestHeaders.Add("X-Actor-Provenance", ComputeProvenanceProof(personId));
        client.DefaultRequestHeaders.Add("X-Web-Session-Token", SignDemoSessionToken(DemoSessionSecret));
        return client;
    }

    private static string FeatureConfigPath(string tenantId = SyntheticTenantId) => $"/api/v1/tenants/{tenantId}/feature-configuration";

    [Fact]
    public async Task Admin_can_read_and_write_the_seeded_synthetic_tenants_configuration_a_real_granted_result_not_just_permission_required()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = ClientAs(AdminPersonId);

        var getResponse = await client.GetAsync(FeatureConfigPath(), ct);
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var config = await getResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        var version = config.GetProperty("version").GetInt32();
        var cardProductionBefore = config.GetProperty("card_production_enabled").GetBoolean();

        var patchResponse = await client.PatchAsJsonAsync(
            FeatureConfigPath(), new { version, card_production_enabled = !cardProductionBefore }, ct);
        Assert.Equal(HttpStatusCode.OK, patchResponse.StatusCode);
        var updated = await patchResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal(!cardProductionBefore, updated.GetProperty("card_production_enabled").GetBoolean());

        // Confirms the write genuinely persisted server-side, the same "GET after PATCH" shape
        // TenantFeatureConfigurationEndpointsIntegrationTests.Patch_then_get_reflects_the_update_end_to_end
        // already proves within this story's own project - now proven through the real host,
        // the real RBAC engine, and a seeded (not faked) permission grant.
        var confirmResponse = await client.GetAsync(FeatureConfigPath(), ct);
        Assert.Equal(HttpStatusCode.OK, confirmResponse.StatusCode);
        var confirmed = await confirmResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal(!cardProductionBefore, confirmed.GetProperty("card_production_enabled").GetBoolean());
    }

    [Fact]
    public async Task Viewer_can_read_but_a_write_attempt_is_denied_with_a_real_403_proving_differentiated_rbac_not_a_blanket_rejection()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = ClientAs(ViewerPersonId);

        var getResponse = await client.GetAsync(FeatureConfigPath(), ct);
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var config = await getResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        var version = config.GetProperty("version").GetInt32();

        var patchResponse = await client.PatchAsJsonAsync(
            FeatureConfigPath(), new { version, card_production_enabled = true }, ct);

        Assert.Equal(HttpStatusCode.Forbidden, patchResponse.StatusCode);
        var body = await patchResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("forbidden", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_well_formed_but_unseeded_actor_is_denied_reading_too_proving_the_fail_closed_default_not_just_rejecting_malformed_ids()
    {
        var ct = TestContext.Current.CancellationToken;
        // Well-formed per_<UUIDv7> (passes CanonicalId.IsPersonId), but neither a seeded Person
        // row nor a seeded RolePermissionGrant - distinct from a malformed-id rejection, which
        // ActorHasPermissionAsync's own format check already covers separately.
        var client = ClientAs("per_01a113f6-6268-7000-8000-000000000099");

        var response = await client.GetAsync(FeatureConfigPath(), ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("forbidden", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task With_no_actor_id_header_at_all_the_request_is_rejected_before_any_provenance_permission_or_tenant_check()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = _factory.CreateClient();

        var response = await client.GetAsync(FeatureConfigPath(), ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("actor_required", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task With_an_actor_id_but_no_provenance_proof_the_request_is_rejected_401_unchanged_from_before_this_fix()
    {
        // The same pre-bridge negative the superseded live spec proved - a caller asserting an
        // actor id with no provenance proof is still correctly rejected, regardless of anything
        // this round's seeding added.
        var ct = TestContext.Current.CancellationToken;
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Actor-Id", AdminPersonId);

        var response = await client.GetAsync(FeatureConfigPath(), ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("actor_provenance_required", body.GetProperty("code").GetString());
    }
}

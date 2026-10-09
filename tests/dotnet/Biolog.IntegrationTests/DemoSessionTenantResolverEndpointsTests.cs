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
/// End-to-end proof, through the REAL packaged <c>Biolog.Api</c> host, that
/// TD-WEB-PROVENANCE-BRIDGE round 5's demo-session tenant confinement
/// (<see cref="Biolog.Api.Tenancy.DemoSessionTenantResolver"/>) is genuinely wired in - not
/// just correct in <c>Biolog.UnitTests.Tenancy.TenantContextMiddlewareTests</c>'s own
/// hand-constructed middleware instance.
///
/// Deliberately does NOT use <c>WithTenantSwitching()</c>'s header-switched
/// <c>ITenantContextAccessor</c> replacement the way <c>ApiSecurityEndpointsTests</c>/
/// <c>AuthenticationEndpointsTests</c> do - that replacement bypasses
/// <see cref="Biolog.Api.Tenancy.TenantContextMiddleware"/>'s own real
/// <c>TenantContextAccessor.SetResolved</c> call entirely, which is exactly the code path this
/// story adds to. These tests use the real, unreplaced concrete accessor registration instead.
///
/// Target endpoint: <c>POST /api/v1/integrations/service-accounts</c> (BIO-INT-003's own
/// create-service-account route) - reused because its exact behaviour at every layer
/// (actor_required, tenant_context_missing, permission_required) is already fully
/// characterised by BIO-INT-003's own test suite, and because no test project has
/// <c>InternalsVisibleTo</c> access to seed a real permission grant anywhere in this codebase -
/// the same disclosed limitation <c>ApiSecurityEndpointsTests</c>'s own header already names.
/// A genuinely confined demo session therefore reaches as far as `permission_required` (403) -
/// proof tenant resolution succeeded and the request reached the real permission check - never
/// a bare success, which this test setup cannot honestly produce without that seeding access.
/// </summary>
public sealed class DemoSessionTenantResolverEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private const string ProvenanceSecret = "test-host-e2e-actor-provenance-secret-demo-bridge";
    private const string DemoSessionSecret = "test-host-e2e-web-session-bridge-secret-demo-bridge";
    private const string SyntheticTenantId = "ten_01a113cb-0d05-774b-a463-a954598ed2e9";
    private const string TestActorId = "per_test_unprivileged_actor";

    public DemoSessionTenantResolverEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder
            .UseEnvironment("Development")
            .ConfigureAppConfiguration(cfg => cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ApiSecurity:ActorProvenanceSecret"] = ProvenanceSecret,
                ["WebSessionBridge:TokenSecret"] = DemoSessionSecret,
                ["WebSessionBridge:SyntheticTenantId"] = SyntheticTenantId,
            })));
    }

    private static string ComputeProvenanceProof(string actorId) =>
        Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(ProvenanceSecret), Encoding.UTF8.GetBytes(actorId)));

    /// <summary>Signs a demo session token exactly the way web/lib/session-token.ts does - mirrored here, not imported, since this is a cross-stack (TS signer / C# verifier) boundary by design.</summary>
    private static string SignDemoSessionToken(string secret, string roleId = "admin", string tenantId = "alpha", long? expiresAtMs = null)
    {
        var claims = new
        {
            sid = $"sid_{Guid.NewGuid():N}",
            roleId,
            tenantId,
            issuedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            expiresAtMs = expiresAtMs ?? DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds(),
        };
        var json = JsonSerializer.Serialize(claims);
        var payloadB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        var signature = Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(payloadB64)));
        return $"{payloadB64}.{signature}";
    }

    private HttpClient ClientWithValidActor()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Actor-Id", TestActorId);
        client.DefaultRequestHeaders.Add("X-Actor-Provenance", ComputeProvenanceProof(TestActorId));
        return client;
    }

    private static Task<HttpResponseMessage> CreateServiceAccountAsync(HttpClient client, CancellationToken ct) =>
        client.PostAsJsonAsync("/api/v1/integrations/service-accounts", new { displayName = "Demo bridge probe", scopes = new[] { "ReadOnly" } }, ct);

    private static async Task<string> ProblemTitleAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        return body.GetProperty("title").GetString() ?? string.Empty;
    }

    [Fact]
    public async Task No_demo_session_token_at_all_resolves_exactly_as_before_this_story_tenant_context_missing()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = ClientWithValidActor();

        var response = await CreateServiceAccountAsync(client, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("tenant_context_missing", await ProblemTitleAsync(response, ct));
    }

    [Fact]
    public async Task A_genuinely_signed_demo_session_token_confines_resolution_to_the_synthetic_tenant_and_reaches_the_real_permission_check()
    {
        // The core proof of round 5: tenant resolution now genuinely succeeds (no more
        // tenant_context_missing) and reaches the REAL permission gate (permission_required,
        // 403) - not a bare success (no grant exists for the synthetic tenant, the same
        // disclosed test-seeding limitation every sibling story's own host-channel tests share)
        // and not a privilege escalation (the request still cannot create a service account).
        var ct = TestContext.Current.CancellationToken;
        var client = ClientWithValidActor();
        client.DefaultRequestHeaders.Add("X-Web-Session-Token", SignDemoSessionToken(DemoSessionSecret, roleId: "admin"));

        var response = await CreateServiceAccountAsync(client, ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("permission_required", await ProblemTitleAsync(response, ct));
    }

    [Fact]
    public async Task The_demo_session_token_s_own_claimed_role_has_no_bearing_on_the_outcome_self_selecting_admin_is_still_confined()
    {
        // ZB-WEBPROV-02's own point: self-selecting 'admin' must stay harmless. Both an
        // admin-claimed and a viewer-claimed demo session land in the exact same confined
        // outcome - the claim is a UI-level convenience, never a backend privilege lever.
        var ct = TestContext.Current.CancellationToken;
        var adminClient = ClientWithValidActor();
        adminClient.DefaultRequestHeaders.Add("X-Web-Session-Token", SignDemoSessionToken(DemoSessionSecret, roleId: "admin"));
        var viewerClient = ClientWithValidActor();
        viewerClient.DefaultRequestHeaders.Add("X-Web-Session-Token", SignDemoSessionToken(DemoSessionSecret, roleId: "viewer"));

        var adminResponse = await CreateServiceAccountAsync(adminClient, ct);
        var viewerResponse = await CreateServiceAccountAsync(viewerClient, ct);

        Assert.Equal(HttpStatusCode.Forbidden, adminResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, viewerResponse.StatusCode);
    }

    [Fact]
    public async Task A_tampered_demo_session_token_is_treated_as_absent_falls_back_to_tenant_context_missing_never_a_false_confinement()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = ClientWithValidActor();
        var genuine = SignDemoSessionToken(DemoSessionSecret);
        var tampered = genuine[..^4] + "0000";
        client.DefaultRequestHeaders.Add("X-Web-Session-Token", tampered);

        var response = await CreateServiceAccountAsync(client, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("tenant_context_missing", await ProblemTitleAsync(response, ct));
    }

    [Fact]
    public async Task An_expired_demo_session_token_falls_back_to_tenant_context_missing()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = ClientWithValidActor();
        client.DefaultRequestHeaders.Add("X-Web-Session-Token", SignDemoSessionToken(DemoSessionSecret, expiresAtMs: DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeMilliseconds()));

        var response = await CreateServiceAccountAsync(client, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("tenant_context_missing", await ProblemTitleAsync(response, ct));
    }

    [Fact]
    public async Task A_demo_session_token_signed_with_the_wrong_secret_is_rejected_never_trusted()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = ClientWithValidActor();
        client.DefaultRequestHeaders.Add("X-Web-Session-Token", SignDemoSessionToken("a-completely-different-synthetic-secret-value-here"));

        var response = await CreateServiceAccountAsync(client, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("tenant_context_missing", await ProblemTitleAsync(response, ct));
    }
}

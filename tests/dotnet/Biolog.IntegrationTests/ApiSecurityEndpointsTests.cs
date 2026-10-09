using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Biolog.Api.Tenancy;
using Biolog.Features.ApiSecurity;
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
/// Developer tests for BIO-INT-003, exercised through the real composed host: the real
/// <see cref="Api.Modules.BioInt003Module"/> DI wiring. Uses the same header-switched tenant
/// test seam <see cref="AuthenticationEndpointsTests"/> established for tenant resolution, and
/// the same real-provenance-secret-via-<c>ConfigureAppConfiguration</c> seam
/// <c>RbacHostChannelTests</c>/<c>AccessPolicyHostChannelTests</c> already established for
/// actor provenance (never a test-only backdoor into the gate itself).
///
/// <b>Same disclosed limitation as those host-channel tests (security-review fix, Zainab,
/// ZB-INT003-02).</b> This project has no <c>InternalsVisibleTo</c> access to seed a real
/// permission grant into BIO-IDN-006's internals, so every create/list/rotate/revoke call here
/// can only demonstrate a request reaching <see cref="ServiceAccountService"/> through the real
/// host and being rejected for lack of authority - never a management mutation SUCCEEDING via
/// HTTP. The underlying AC01/AC04 positive-path proofs (tenant isolation, secret hashing,
/// rotation/revocation semantics), with a permission gateway that grants, live at the unit level
/// in <c>ServiceAccountServiceTests.cs</c>. AC02 (OAuth2 grant)/AC03 (rate limiting)/<c>whoami</c>
/// need no actor or permission at all (see <c>BioInt003Module</c>'s own remarks) and ARE proven
/// here end-to-end, using a service account seeded directly into a substituted
/// <see cref="IServiceAccountRepository"/> instance (a public interface, not an internal type -
/// this bypasses only the gated CREATE endpoint, never the permission gate itself, which every
/// create/list/rotate/revoke test below still exercises for real).
/// </summary>
public sealed class ApiSecurityEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _rawFactory;
    private readonly WebApplicationFactory<Program> _factory;
    private const string TestTenantHeader = "X-Test-Tenant-Id";
    private const string AccessTokenSecret = "test-host-e2e-access-token-secret-bio-int-003-aaaaaaaaaaaa";
    private const string SecretHashPepper = "test-host-e2e-secret-hash-pepper-bio-int-003-aaaaaaaaaaaaaa";
    private const string ProvenanceSecret = "test-host-e2e-actor-provenance-secret-bio-int-003";
    private const string TestActorId = "per_test_unprivileged_actor";

    public ApiSecurityEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _rawFactory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
        _factory = _rawFactory.WithWebHostBuilder(builder => builder
            .ConfigureAppConfiguration(cfg => cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ApiSecurity:AccessTokenSecret"] = AccessTokenSecret,
                ["ApiSecurity:SecretHashPepper"] = SecretHashPepper,
                ["ApiSecurity:ActorProvenanceSecret"] = ProvenanceSecret,
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

    /// <summary>A real, in-process-seeded service account - bypasses only the gated CREATE
    /// endpoint (never the permission gate on rotate/revoke, and never the token/whoami
    /// endpoints, which need no permission at all). <see cref="HmacApiSecretHasher"/> here uses
    /// the SAME pepper this test's host is configured with, so the resulting hash verifies
    /// against the real <c>IApiSecretHasher</c> instance the host resolves at request time.</summary>
    private static (WebApplicationFactory<Program> Factory, string ClientId, string PlaintextSecret, InMemoryServiceAccountRepository Repository) WithPreseededServiceAccount(
        WebApplicationFactory<Program> baseFactory, string tenantId, IReadOnlySet<string> scopes)
    {
        var plaintextSecret = "preseeded-secret-" + Guid.NewGuid().ToString("N");
        var clientId = Guid.NewGuid().ToString();
        var repository = new InMemoryServiceAccountRepository();
        repository.CreateAsync(tenantId, clientId, "Preseeded integrator", scopes, new HmacApiSecretHasher(SecretHashPepper).Hash(plaintextSecret), CancellationToken.None)
            .GetAwaiter().GetResult();

        var factory = baseFactory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IServiceAccountRepository>();
            services.AddSingleton<IServiceAccountRepository>(repository);
        }));

        return (factory, clientId, plaintextSecret, repository);
    }

    private static HttpClient CreateClientForTenant(WebApplicationFactory<Program> factory, string tenantId, bool withValidActor = true)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestTenantHeader, tenantId);
        if (withValidActor)
        {
            client.DefaultRequestHeaders.Add("X-Actor-Id", TestActorId);
            client.DefaultRequestHeaders.Add("X-Actor-Provenance", ComputeProvenanceProof(TestActorId));
        }

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

    // --- Security-review fix (Zainab, ZB-INT003-02, Critical): actor + provenance + permission gate ---

    [Fact]
    public async Task ZB02_a_request_missing_the_actor_header_is_rejected_with_400()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = CreateClientForTenant(WithTenantSwitching(), "ten_int003_zb02_no_actor", withValidActor: false);

        var response = await client.PostAsJsonAsync(
            "/api/v1/integrations/service-accounts",
            new { displayName = "No actor", scopes = new[] { "ReadOnly" } },
            ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("actor_required", body.GetProperty("title").GetString());
    }

    [Fact]
    public async Task ZB02_a_request_with_a_forged_or_garbage_provenance_proof_is_rejected_with_401()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = CreateClientForTenant(WithTenantSwitching(), "ten_int003_zb02_forged", withValidActor: false);
        client.DefaultRequestHeaders.Add("X-Actor-Id", TestActorId);
        client.DefaultRequestHeaders.Add("X-Actor-Provenance", "0000000000000000000000000000000000000000000000000000000000000000");

        var response = await client.PostAsJsonAsync(
            "/api/v1/integrations/service-accounts",
            new { displayName = "Forged actor", scopes = new[] { "ReadOnly" } },
            ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("actor_provenance_required", body.GetProperty("title").GetString());
    }

    [Fact]
    public async Task ZB02_a_resolved_actor_with_valid_provenance_but_no_grant_cannot_create_a_service_account()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = CreateClientForTenant(WithTenantSwitching(), "ten_int003_zb02_create_denied");

        var response = await client.PostAsJsonAsync(
            "/api/v1/integrations/service-accounts",
            new { displayName = "Unauthorised attempt", scopes = new[] { "ReadOnly" } },
            ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("permission_required", body.GetProperty("title").GetString());
        Assert.Equal("integrations.service_account_manage", body.GetProperty("permission_key").GetString());
    }

    [Fact]
    public async Task ZB02_a_resolved_actor_with_valid_provenance_but_no_grant_cannot_list_service_accounts()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = CreateClientForTenant(WithTenantSwitching(), "ten_int003_zb02_list_denied");

        var response = await client.GetAsync("/api/v1/integrations/service-accounts", ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("integrations.service_account_read", body.GetProperty("permission_key").GetString());
    }

    [Fact]
    public async Task ZB02_a_resolved_actor_with_valid_provenance_but_no_grant_cannot_rotate_or_revoke_an_existing_account()
    {
        var ct = TestContext.Current.CancellationToken;
        const string tenantId = "ten_int003_zb02_rotate_revoke_denied";
        var (factory, clientId, _, _) = WithPreseededServiceAccount(WithTenantSwitching(), tenantId, new HashSet<string> { "ReadOnly" });
        var client = CreateClientForTenant(factory, tenantId);

        var rotateResponse = await client.PostAsJsonAsync($"/api/v1/integrations/service-accounts/{clientId}/rotate-secret", new { expectedVersion = 1 }, ct);
        var revokeResponse = await client.PostAsJsonAsync($"/api/v1/integrations/service-accounts/{clientId}/revoke", new { expectedVersion = 1 }, ct);

        Assert.Equal(HttpStatusCode.Forbidden, rotateResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, revokeResponse.StatusCode);
    }

    // --- Security-review fix (Zainab, ZB-INT003-05, Critical - round 2): no public dummy-key fallback ---

    [Fact]
    public async Task ZB05_the_former_public_dummy_key_proof_no_longer_satisfies_an_unconfigured_provenance_gate()
    {
        // _rawFactory has NO ApiSecurity:* configuration at all - this is the exact "unconfigured"
        // state her review exploited. Computing the OLD vulnerable dummy key
        // (SHA256 of the literal "\0no-provenance-secret-configured\0", exactly as the removed
        // code did) and using it to assert a PRIVILEGED actor id must still be rejected - a real
        // attacker reading the old source could compute this same value.
        var ct = TestContext.Current.CancellationToken;
        var client = _rawFactory.CreateClient();
        const string privilegedActorId = "per_super_admin_victim";
        var formerPublicDummyKey = SHA256.HashData(Encoding.UTF8.GetBytes("\0no-provenance-secret-configured\0"));
        var formerDummyKeyProof = Convert.ToHexString(HMACSHA256.HashData(formerPublicDummyKey, Encoding.UTF8.GetBytes(privilegedActorId)));

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/integrations/service-accounts")
        {
            Content = JsonContent.Create(new { displayName = "x", scopes = new[] { "ReadOnly" } }),
        };
        request.Headers.Add("X-Actor-Id", privilegedActorId);
        request.Headers.Add("X-Actor-Provenance", formerDummyKeyProof);

        var response = await client.SendAsync(request, ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // --- Security-review fix (Zainab, ZB-INT003-03, High - round 2, reopened): live-state re-check ---

    [Fact]
    public async Task ZB03_a_revoked_accounts_token_is_rejected_at_whoami_even_though_it_has_not_expired()
    {
        // SuperAdmin is no longer an issuable scope (BIO-INT-003-ZB-07, round 4), so this test
        // can no longer seed a "passes with no grant needed" starting point - it instead proves
        // revocation produces a STRICTER, DISTINCT denial (401, account gone) than the "no real
        // grant exists" denial (403, insufficient permission) the account would already get
        // BEFORE revocation, isolating that revocation itself is independently checked.
        var ct = TestContext.Current.CancellationToken;
        var (factory, clientId, secret, repository) = WithPreseededServiceAccount(_factory, "ten_int003_zb03_revoked", new HashSet<string> { "ReadOnly" });
        var client = factory.CreateClient();

        var tokenResponse = await client.PostAsJsonAsync(
            "/api/v1/oauth/token",
            new { grant_type = "client_credentials", client_id = clientId, client_secret = secret, scope = (string?)null },
            ct);
        var token = (await tokenResponse.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("access_token").GetString();

        var beforeRevocation = await SendWhoamiAsync(client, token!, ct);
        Assert.Equal(HttpStatusCode.Forbidden, beforeRevocation.StatusCode); // no real grant yet, but the account itself is still valid

        // Revoke directly against the repository this host resolves - models "an admin revoked
        // this account through some other legitimate path," not a gated-endpoint bypass.
        await repository.RevokeAsync("ten_int003_zb03_revoked", clientId, 1, ct);

        var afterRevocation = await SendWhoamiAsync(client, token!, ct);

        Assert.Equal(HttpStatusCode.Unauthorized, afterRevocation.StatusCode); // the account itself is now gone, a stricter denial than mere insufficient-scope
    }

    [Fact]
    public async Task ZB03_a_scope_with_no_real_tenant_grant_for_the_required_permission_is_rejected_with_403()
    {
        // Security-review fix (Zainab, ZB-INT003-03, round 3): proves the REAL
        // IRolePermissionGrantRepository is consulted, not a role-label string match - a fresh
        // test tenant has NO grants at all (BIO-IDN-006's own fail-closed default), so even a
        // role that would have passed round 2's literal-label check ("ReadOnly") is denied here.
        var ct = TestContext.Current.CancellationToken;
        var (factory, clientId, secret, _) = WithPreseededServiceAccount(_factory, "ten_int003_zb03_scope", new HashSet<string> { "ReadOnly" });
        var client = factory.CreateClient();

        var tokenResponse = await client.PostAsJsonAsync(
            "/api/v1/oauth/token",
            new { grant_type = "client_credentials", client_id = clientId, client_secret = secret, scope = (string?)null },
            ct);
        var token = (await tokenResponse.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("access_token").GetString();

        var whoamiResponse = await SendWhoamiAsync(client, token!, ct);

        Assert.Equal(HttpStatusCode.Forbidden, whoamiResponse.StatusCode);
        var body = await whoamiResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("insufficient_scope", body.GetProperty("title").GetString());
    }

    // --- Security-review fix (Zainab, ZB-INT003-07, High - round 4): no blanket admin scope ---

    [Fact]
    public async Task ZB07_SuperAdmin_is_rejected_as_a_service_account_scope_at_the_real_http_layer()
    {
        // Scope validation happens BEFORE the permission-gated service call (same ordering
        // ZB01's own unknown-scope test already proves), so this 400 is reachable even for an
        // actor who holds no real grant at all - SuperAdmin is treated exactly like any other
        // string outside the known catalogue, never a special, escalation-capable case.
        var ct = TestContext.Current.CancellationToken;
        var client = CreateClientForTenant(WithTenantSwitching(), "ten_int003_zb07_superadmin_rejected");

        var response = await client.PostAsJsonAsync(
            "/api/v1/integrations/service-accounts",
            new { displayName = "Blanket admin attempt", scopes = new[] { "SuperAdmin" } },
            ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("unknown_scope", body.GetProperty("title").GetString());
    }

    private static Task<HttpResponseMessage> SendWhoamiAsync(HttpClient client, string token, CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/integrations/service-accounts/whoami");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return client.SendAsync(request, ct);
    }

    [Fact]
    public async Task ZB01_an_unknown_scope_is_still_rejected_before_the_permission_gated_mutation_runs()
    {
        // Scope validation happens in the endpoint handler before the permission-gated service
        // call, so this 400 remains reachable even though this actor holds no real grant.
        var ct = TestContext.Current.CancellationToken;
        var client = CreateClientForTenant(WithTenantSwitching(), "ten_int003_unknown_scope");

        var response = await client.PostAsJsonAsync(
            "/api/v1/integrations/service-accounts",
            new { displayName = "Bad scope", scopes = new[] { "NotARealScope" } },
            ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_missing_tenant_context_is_rejected_even_before_any_scope_or_permission_check()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = _factory.CreateClient(); // no tenant header, no tenant resolution configured
        client.DefaultRequestHeaders.Add("X-Actor-Id", TestActorId);
        client.DefaultRequestHeaders.Add("X-Actor-Provenance", ComputeProvenanceProof(TestActorId));

        var response = await client.PostAsJsonAsync(
            "/api/v1/integrations/service-accounts",
            new { displayName = "No tenant", scopes = new[] { "ReadOnly" } },
            ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // --- AC02/AC03/whoami: no actor or permission involved - proven fully end to end ---

    [Fact]
    public async Task AC02_a_valid_client_credentials_grant_issues_a_bearer_token()
    {
        var ct = TestContext.Current.CancellationToken;
        var (factory, clientId, secret, _) = WithPreseededServiceAccount(_factory, "ten_int003_ac02_grant", new HashSet<string> { "ReadOnly", "Gate" });
        var client = factory.CreateClient();

        var tokenResponse = await client.PostAsJsonAsync(
            "/api/v1/oauth/token",
            new { grant_type = "client_credentials", client_id = clientId, client_secret = secret, scope = (string?)null },
            ct);

        Assert.Equal(HttpStatusCode.OK, tokenResponse.StatusCode);
        var body = await tokenResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.False(string.IsNullOrEmpty(body.GetProperty("access_token").GetString()));
        Assert.Equal("Bearer", body.GetProperty("token_type").GetString());
    }

    [Fact]
    public async Task AC02_a_wrong_secret_is_rejected_with_401()
    {
        var ct = TestContext.Current.CancellationToken;
        var (factory, clientId, _, _) = WithPreseededServiceAccount(_factory, "ten_int003_ac02_wrong_secret", new HashSet<string> { "ReadOnly" });
        var client = factory.CreateClient();

        var tokenResponse = await client.PostAsJsonAsync(
            "/api/v1/oauth/token",
            new { grant_type = "client_credentials", client_id = clientId, client_secret = "not-the-real-secret", scope = (string?)null },
            ct);

        Assert.Equal(HttpStatusCode.Unauthorized, tokenResponse.StatusCode);
    }

    [Fact]
    public async Task AC03_a_well_formed_token_reaches_real_authorization_and_a_garbage_token_fails_signature_validation_first()
    {
        // SuperAdmin is no longer an issuable scope (ZB-INT003-07, round 4), so a genuine ALLOW
        // proof at this layer is no longer available without a real grant this project cannot
        // seed (disclosed limitation - see docs/features/bio-int-003/README.md). This test
        // instead proves the two checks are genuinely SEPARATE stages: a well-formed token with
        // no real grant reaches real authorization logic and is denied there (403), while a
        // garbage token never gets that far at all (401, fails at signature validation).
        var ct = TestContext.Current.CancellationToken;
        var (factory, clientId, secret, _) = WithPreseededServiceAccount(_factory, "ten_int003_ac03_whoami", new HashSet<string> { "ReadOnly" });
        var client = factory.CreateClient();

        var tokenResponse = await client.PostAsJsonAsync(
            "/api/v1/oauth/token",
            new { grant_type = "client_credentials", client_id = clientId, client_secret = secret, scope = (string?)null },
            ct);
        var token = (await tokenResponse.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("access_token").GetString();

        var wellFormedResponse = await SendWhoamiAsync(client, token!, ct);
        Assert.Equal(HttpStatusCode.Forbidden, wellFormedResponse.StatusCode);

        var garbageResponse = await SendWhoamiAsync(client, "not-a-real-token", ct);
        Assert.Equal(HttpStatusCode.Unauthorized, garbageResponse.StatusCode);
    }

    [Fact]
    public async Task AC03_exceeding_the_oauth_token_rate_limit_returns_429_with_a_retry_after_header()
    {
        var ct = TestContext.Current.CancellationToken;
        var (factory, clientId, secret, _) = WithPreseededServiceAccount(_factory, "ten_int003_ac03_rate_limit", new HashSet<string> { "ReadOnly" });
        var client = factory.CreateClient();

        HttpResponseMessage? last = null;
        // Default limit is 60/min (ApiSecurity:RateLimitPerMinute, test host config is unset) -
        // 61 calls against a single client_id must trip the limiter on the last one.
        for (var i = 0; i < 61; i++)
        {
            last = await client.PostAsJsonAsync(
                "/api/v1/oauth/token",
                new { grant_type = "client_credentials", client_id = clientId, client_secret = secret, scope = (string?)null },
                ct);
        }

        Assert.Equal((HttpStatusCode)429, last!.StatusCode);
        Assert.True(last.Headers.RetryAfter is not null);
    }
}

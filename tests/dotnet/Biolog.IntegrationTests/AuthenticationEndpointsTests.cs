using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Biolog.Api.Tenancy;
using Biolog.Features.Authentication;
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
/// Developer tests for BIO-INT-001, exercised through the real composed host: the real
/// <see cref="Api.Modules.BioInt001Module"/> DI wiring and the exact
/// <c>/api/v1/integrations/primary-action</c> path <c>web/features/integrations/live.ts</c>
/// (BIO-UX-005) already targets for its <c>IntegrationService</c> module (BIO-INT-001-AC05).
/// Uses the same header-switched tenant test seam <c>DeviceRegistryTests</c>/
/// <c>NotificationEndpointsTests</c> established, for the same reason documented there.
/// </summary>
public sealed class AuthenticationEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private const string TestTenantHeader = "X-Test-Tenant-Id";

    // TD-PROVENANCE-DUMMYKEY-SWEEP fallout (god, 2026-10-07): HmacSessionTokenCodec.Issue() now
    // fails closed when Authentication:SessionTokenSecret is unconfigured or under 32 chars - the
    // old shared dummy key silently masked that this key was never actually set anywhere. Every
    // sibling *HostChannelTests file already configures its own secret this same way (see
    // RbacHostChannelTests.ProvenanceSecret); this file just never needed one before.
    private const string SessionTokenSecret = "auth-endpoints-tests-session-token-secret-32ok";

    public AuthenticationEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder
            .UseEnvironment("Development")
            .ConfigureAppConfiguration(cfg => cfg.AddInMemoryCollection(
                new Dictionary<string, string?> { ["Authentication:SessionTokenSecret"] = SessionTokenSecret })));
    }

    private WebApplicationFactory<Program> WithTenantSwitching() =>
        _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ITenantContextAccessor>();
            services.AddHttpContextAccessor();
            services.AddScoped<ITenantContextAccessor, HeaderSwitchedTenantContextAccessor>();
        }));

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
    public async Task AC05_primary_action_runs_the_real_pipeline_including_mfa_and_reports_mapped_roles()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = CreateClientForTenant(WithTenantSwitching(), "ten_int001_ac05");

        var response = await client.PostAsync("/api/v1/integrations/primary-action", content: null, ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        var message = body.GetProperty("message").GetString();
        Assert.Contains("MFA verified", message);
        Assert.Contains("RegistrarHr", message);
    }

    [Fact]
    public async Task AC05_primary_action_is_idempotent_about_the_demo_provider_it_seeds()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = CreateClientForTenant(WithTenantSwitching(), "ten_int001_ac05_idempotent");

        await client.PostAsync("/api/v1/integrations/primary-action", content: null, ct);
        await client.PostAsync("/api/v1/integrations/primary-action", content: null, ct);

        var listResponse = await client.GetAsync("/api/v1/auth-providers", ct);
        var providers = await listResponse.Content.ReadFromJsonAsync<List<AuthProviderConfig>>(ct);

        Assert.Single(providers!); // second call reused the same seeded provider, did not register a duplicate
    }

    [Fact]
    public async Task primary_action_fails_closed_with_no_resolved_tenant_context()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsync("/api/v1/integrations/primary-action", content: null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AC01_a_provider_registered_under_one_tenant_is_invisible_to_another()
    {
        var ct = TestContext.Current.CancellationToken;
        var factory = WithTenantSwitching();
        var clientA = CreateClientForTenant(factory, "ten_int001_scope_a");
        var clientB = CreateClientForTenant(factory, "ten_int001_scope_b");

        var created = await clientA.PostAsJsonAsync(
            "/api/v1/auth-providers",
            new AuthProviderRegistrationRequest(AuthProviderType.Password, "Tenant A password", MfaRequirement.None, []), ct);
        var config = await created.Content.ReadFromJsonAsync<AuthProviderConfig>(ct);

        var seenByB = await clientB.GetAsync($"/api/v1/auth-providers/{config!.ProviderId}", ct);

        Assert.Equal(HttpStatusCode.NotFound, seenByB.StatusCode);
    }

    [Fact]
    public async Task AC02_end_to_end_authentication_only_grants_explicitly_mapped_roles()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = CreateClientForTenant(WithTenantSwitching(), "ten_int001_ac02");

        var created = await client.PostAsJsonAsync(
            "/api/v1/auth-providers",
            new AuthProviderRegistrationRequest(
                AuthProviderType.Password, "AC02 provider", MfaRequirement.None,
                [new ClaimMappingRule("group", "registrars", "RegistrarHr")]),
            ct);
        var config = await created.Content.ReadFromJsonAsync<AuthProviderConfig>(ct);

        await client.PostAsJsonAsync(
            $"/api/v1/auth-providers/{config!.ProviderId}/password-credentials",
            new PasswordCredentialRegistrationRequest("bob", "correct-password", [new ProviderClaim("group", "registrars"), new ProviderClaim("group", "unmapped")]),
            ct);

        var authResponse = await client.PostAsJsonAsync(
            $"/api/v1/auth/{config.ProviderId}/authenticate",
            new ProviderCredentialInput(new Dictionary<string, string> { ["username"] = "bob", ["password"] = "correct-password" }, []),
            ct);

        Assert.Equal(HttpStatusCode.OK, authResponse.StatusCode);
        var body = await authResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("success", body.GetProperty("outcome").GetString());
        var roles = body.GetProperty("roles").EnumerateArray().Select(r => r.GetString()).ToList();
        Assert.Equal(["RegistrarHr"], roles); // the "unmapped" claim never became a role
    }

    [Fact]
    public async Task Registering_a_provider_with_an_unknown_role_mapping_is_rejected()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = CreateClientForTenant(WithTenantSwitching(), "ten_int001_unknown_role");

        var response = await client.PostAsJsonAsync(
            "/api/v1/auth-providers",
            new AuthProviderRegistrationRequest(
                AuthProviderType.Password, "Bad mapping", MfaRequirement.None,
                [new ClaimMappingRule("group", "owners", "GlobalOwner")]),
            ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AC03_the_mfa_required_response_never_exposes_a_code_and_a_session_is_never_issued_alongside_it()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = CreateClientForTenant(WithTenantSwitching(), "ten_int001_ac03");

        var created = await client.PostAsJsonAsync(
            "/api/v1/auth-providers",
            new AuthProviderRegistrationRequest(AuthProviderType.Password, "AC03 provider", MfaRequirement.Required, []), ct);
        var config = await created.Content.ReadFromJsonAsync<AuthProviderConfig>(ct);

        await client.PostAsJsonAsync(
            $"/api/v1/auth-providers/{config!.ProviderId}/password-credentials",
            new PasswordCredentialRegistrationRequest("carol", "pw", []), ct);

        var authResponse = await client.PostAsJsonAsync(
            $"/api/v1/auth/{config.ProviderId}/authenticate",
            new ProviderCredentialInput(new Dictionary<string, string> { ["username"] = "carol", ["password"] = "pw" }, []), ct);
        var authBody = await authResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("mfa_required", authBody.GetProperty("outcome").GetString());

        // BIO-INT-001-F01 (Zainab, High) fix: the mfa_required response carries exactly
        // outcome/challengeId/expiresAt - never a code, never a session token.
        var responseFields = authBody.EnumerateObject().Select(p => p.Name).ToHashSet();
        Assert.Equal(new HashSet<string> { "outcome", "challengeId", "expiresAt" }, responseFields);
    }

    /// <summary>BIO-INT-001-F01 (Zainab, High): the regression test she explicitly asked for -
    /// a caller who has ONLY the challenge id returned by a real HTTP response (the exact
    /// information an external attacker would have) cannot compute or guess a code that
    /// verifies. This is the honest, disclosed flip side of the fix: since no real out-of-band
    /// transport exists yet, a real MFA-required login genuinely cannot be completed through
    /// the live endpoints today (see docs/features/bio-int-001/README.md's AC03 section) -
    /// that is a limitation, not a bypass.</summary>
    [Fact]
    public async Task An_attacker_with_only_the_http_visible_challenge_id_cannot_verify_mfa()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = CreateClientForTenant(WithTenantSwitching(), "ten_int001_f01");

        var created = await client.PostAsJsonAsync(
            "/api/v1/auth-providers",
            new AuthProviderRegistrationRequest(AuthProviderType.Password, "F01 provider", MfaRequirement.Required, []), ct);
        var config = await created.Content.ReadFromJsonAsync<AuthProviderConfig>(ct);
        await client.PostAsJsonAsync(
            $"/api/v1/auth-providers/{config!.ProviderId}/password-credentials",
            new PasswordCredentialRegistrationRequest("dana", "pw", []), ct);

        var authResponse = await client.PostAsJsonAsync(
            $"/api/v1/auth/{config.ProviderId}/authenticate",
            new ProviderCredentialInput(new Dictionary<string, string> { ["username"] = "dana", ["password"] = "pw" }, []), ct);
        var authBody = await authResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        var challengeId = authBody.GetProperty("challengeId").GetString()!;

        // The exact formula the earlier, vulnerable revision used - proves it no longer works.
        var oldVulnerableFormula = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(challengeId)))[..6];
        var attackResponse = await client.PostAsJsonAsync($"/api/v1/auth/mfa/{challengeId}/verify", new MfaVerificationRequest(oldVulnerableFormula), ct);

        Assert.Equal(HttpStatusCode.Unauthorized, attackResponse.StatusCode);
    }

    [Fact]
    public async Task AC04_a_failed_login_is_returned_as_401_with_a_closed_reason_code_never_a_raw_exception()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = CreateClientForTenant(WithTenantSwitching(), "ten_int001_ac04");

        var created = await client.PostAsJsonAsync(
            "/api/v1/auth-providers",
            new AuthProviderRegistrationRequest(AuthProviderType.Password, "AC04 provider", MfaRequirement.None, []), ct);
        var config = await created.Content.ReadFromJsonAsync<AuthProviderConfig>(ct);
        await client.PostAsJsonAsync(
            $"/api/v1/auth-providers/{config!.ProviderId}/password-credentials",
            new PasswordCredentialRegistrationRequest("dave", "the-real-password", []), ct);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/auth/{config.ProviderId}/authenticate",
            new ProviderCredentialInput(new Dictionary<string, string> { ["username"] = "dave", ["password"] = "wrong-guess" }, []), ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("InvalidCredentials", body.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task provider_status_change_with_a_stale_expected_version_returns_a_conflict()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = CreateClientForTenant(WithTenantSwitching(), "ten_int001_conflict");

        var created = await client.PostAsJsonAsync(
            "/api/v1/auth-providers",
            new AuthProviderRegistrationRequest(AuthProviderType.Password, "Conflict provider", MfaRequirement.None, []), ct);
        var config = await created.Content.ReadFromJsonAsync<AuthProviderConfig>(ct);

        var response = await client.PatchAsJsonAsync(
            $"/api/v1/auth-providers/{config!.ProviderId}/status", new AuthProviderStatusChangeRequest(false, ExpectedVersion: 999), ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task a_disabled_provider_cannot_authenticate()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = CreateClientForTenant(WithTenantSwitching(), "ten_int001_disabled");

        var created = await client.PostAsJsonAsync(
            "/api/v1/auth-providers",
            new AuthProviderRegistrationRequest(AuthProviderType.Password, "Disable-me", MfaRequirement.None, []), ct);
        var config = await created.Content.ReadFromJsonAsync<AuthProviderConfig>(ct);
        await client.PostAsJsonAsync(
            $"/api/v1/auth-providers/{config!.ProviderId}/password-credentials",
            new PasswordCredentialRegistrationRequest("erin", "pw", []), ct);
        await client.PatchAsJsonAsync($"/api/v1/auth-providers/{config.ProviderId}/status", new AuthProviderStatusChangeRequest(false, config.Version), ct);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/auth/{config.ProviderId}/authenticate",
            new ProviderCredentialInput(new Dictionary<string, string> { ["username"] = "erin", ["password"] = "pw" }, []), ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}

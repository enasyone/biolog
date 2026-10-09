using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Biolog.Features.TenantFeatureConfig.Api;
using Biolog.Features.TenantFeatureConfig.Audit;
using Biolog.Features.TenantFeatureConfig.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Biolog.Features.TenantFeatureConfig.Tests;

/// <summary>
/// BIO-TEN-003-AC05: real HTTP pipeline tests for this story's own endpoints, self-hosted via
/// <see cref="TestServer"/> - mirrors BIO-TEN-001's own <c>TenantEndpointsIntegrationTests</c>
/// pattern exactly: wires this project's own DI registrations and
/// <see cref="TenantFeatureConfigurationEndpoints.Map"/> directly, never through
/// <c>Biolog.Api.Modules.BioTen003Module</c> (that adapter lives in
/// <c>api/Biolog.Api/Modules/</c>, and this project has no <c>ProjectReference</c> back to
/// <c>Biolog.Api.csproj</c> - the one-way dependency <c>scripts/check-module-project-references.sh</c>
/// enforces). This registration list must stay identical in substance to
/// <c>BioTen003Module.RegisterServices</c>/<c>MapEndpoints</c>.
///
/// <b>Round-2 remediation (ZB-TEN003-01/BIO-TEN-003-F01, Critical).</b> Wires a real
/// <see cref="HmacActorProvenanceStore"/> (test secret, same shape as BIO-MOB-005's own
/// <c>EnrolmentEndpointsIntegrationTests</c>) plus <see cref="FakePermissionGateway"/>/
/// <see cref="FakeTenantExistenceGateway"/>, both default-permissive so every pre-existing
/// scenario below keeps proving the SAME behaviour it always did, now through the real
/// provenance/permission/existence pipeline rather than around it.
/// </summary>
public sealed class TenantFeatureConfigurationEndpointsIntegrationTests : IAsyncLifetime
{
    // Round-3 remediation (BIO-TEN-003-F05): must meet HmacActorProvenanceStore.MinimumSecretLength (32 bytes) or Configure nulls the key and every proof fails.
    private const string ProvenanceSecret = "test-provenance-secret-32-bytes!";
    private const string ActorId = "per_018f2e5a-1234-7abc-89ab-0123456789ab";

    private IHost _host = null!;
    private HttpClient _client = null!;
    private FakePermissionGateway _permissionGateway = null!;
    private FakeTenantExistenceGateway _tenantExistenceGateway = null!;

    public async ValueTask InitializeAsync()
    {
        _permissionGateway = new FakePermissionGateway();
        _tenantExistenceGateway = new FakeTenantExistenceGateway();

        var provenanceStore = new HmacActorProvenanceStore();
        provenanceStore.Configure(ProvenanceSecret);

        var builder = new HostBuilder().ConfigureWebHost(webHost =>
        {
            webHost.UseTestServer();
            webHost.ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddSingleton<ITenantFeatureConfigurationStore, InMemoryTenantFeatureConfigurationStore>();
                services.AddSingleton<ITenantFeatureConfigurationAuditSink, InMemoryTenantFeatureConfigurationAuditSink>();
                services.AddSingleton<TenantFeatureConfigurationService>();
                services.AddSingleton<IPermissionGateway>(_permissionGateway);
                services.AddSingleton<ITenantExistenceGateway>(_tenantExistenceGateway);
                services.AddSingleton<IActorProvenanceStore>(provenanceStore);
                services.AddSingleton<ActorProvenanceGate>();
            });
            webHost.Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(TenantFeatureConfigurationEndpoints.Map);
            });
        });

        _host = await builder.StartAsync(TestContext.Current.CancellationToken);
        _client = _host.GetTestClient();
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _host.StopAsync(TestContext.Current.CancellationToken);
        _host.Dispose();
    }

    /// <summary>Default-permissive: grants every permission key for every tenant unless <see cref="Grant"/> is set <c>false</c>.</summary>
    private sealed class FakePermissionGateway : IPermissionGateway
    {
        public bool Grant { get; set; } = true;

        public Task<bool> ActorHasPermissionAsync(string tenantId, string actorId, string permissionKey, CancellationToken cancellationToken = default) =>
            Task.FromResult(Grant);
    }

    /// <summary>Default-permissive: every tenant id "exists" unless explicitly marked absent via <see cref="MarkNonexistent"/>.</summary>
    private sealed class FakeTenantExistenceGateway : ITenantExistenceGateway
    {
        private readonly HashSet<string> _nonexistent = new(StringComparer.Ordinal);

        public void MarkNonexistent(string tenantId) => _nonexistent.Add(tenantId);

        public Task<bool> ExistsAsync(string tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult(!_nonexistent.Contains(tenantId));
    }

    private static string NewTenantId() => $"ten_{Guid.CreateVersion7():D}";

    private static string ProvenanceProof(string actorId) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(ProvenanceSecret), Encoding.UTF8.GetBytes(actorId)));

    private static HttpRequestMessage AdminRequest(HttpMethod method, string url, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add(ActorResolver.ActorIdHeader, ActorId);
        request.Headers.Add(ActorProvenanceGate.ProvenanceHeader, ProvenanceProof(ActorId));
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: TenantFeatureConfigJsonOptions.Strict);
        }

        return request;
    }

    [Fact]
    public async Task Get_for_an_unconfigured_tenant_returns_200_with_every_module_disabled()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenantId = NewTenantId();

        var response = await _client.SendAsync(AdminRequest(HttpMethod.Get, $"/api/v1/tenants/{tenantId}/feature-configuration"), ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        Assert.Equal(0, body.GetProperty("version").GetInt32());
        Assert.False(body.GetProperty("biometrics").GetProperty("enabled").GetBoolean());
        Assert.False(body.GetProperty("biometrics").GetProperty("face_1n_identification_enabled").GetBoolean());
        Assert.False(body.GetProperty("document_verify").GetProperty("enabled").GetBoolean());
    }

    [Fact]
    public async Task Patch_requires_an_actor_id()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenantId = NewTenantId();
        var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/tenants/{tenantId}/feature-configuration")
        {
            Content = JsonContent.Create(new { version = 0, visitor_enabled = true }, options: TenantFeatureConfigJsonOptions.Strict),
        };

        var response = await _client.SendAsync(request, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Patch_then_get_reflects_the_update_end_to_end()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenantId = NewTenantId();

        var patchResponse = await _client.SendAsync(
            AdminRequest(HttpMethod.Patch, $"/api/v1/tenants/{tenantId}/feature-configuration", new { version = 0, visitor_enabled = true }), ct);
        Assert.Equal(HttpStatusCode.OK, patchResponse.StatusCode);
        var patched = await patchResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        Assert.True(patched.GetProperty("visitor_enabled").GetBoolean());
        Assert.Equal(1, patched.GetProperty("version").GetInt32());

        var getResponse = await _client.SendAsync(AdminRequest(HttpMethod.Get, $"/api/v1/tenants/{tenantId}/feature-configuration"), ct);
        var fetched = await getResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        Assert.True(fetched.GetProperty("visitor_enabled").GetBoolean());
    }

    [Fact]
    public async Task Patch_with_a_stale_version_returns_409()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenantId = NewTenantId();
        await _client.SendAsync(
            AdminRequest(HttpMethod.Patch, $"/api/v1/tenants/{tenantId}/feature-configuration", new { version = 0, visitor_enabled = true }), ct);

        var response = await _client.SendAsync(
            AdminRequest(HttpMethod.Patch, $"/api/v1/tenants/{tenantId}/feature-configuration", new { version = 0, attendance_enabled = true }), ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Patch_enabling_biometrics_module_alone_leaves_face_1n_disabled()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenantId = NewTenantId();

        var response = await _client.SendAsync(
            AdminRequest(HttpMethod.Patch, $"/api/v1/tenants/{tenantId}/feature-configuration", new { version = 0, biometrics = new { enabled = true } }), ct);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        Assert.True(body.GetProperty("biometrics").GetProperty("enabled").GetBoolean());
        Assert.False(body.GetProperty("biometrics").GetProperty("face_1n_identification_enabled").GetBoolean());
    }

    [Fact]
    public async Task An_unrecognised_request_field_is_rejected_not_silently_ignored()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenantId = NewTenantId();
        var request = AdminRequest(HttpMethod.Patch, $"/api/v1/tenants/{tenantId}/feature-configuration");
        request.Content = new StringContent("{\"version\":0,\"not_a_real_field\":true}", System.Text.Encoding.UTF8, "application/json");

        var response = await _client.SendAsync(request, ct);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Every_response_carries_a_correlation_id_header()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenantId = NewTenantId();

        var response = await _client.SendAsync(AdminRequest(HttpMethod.Get, $"/api/v1/tenants/{tenantId}/feature-configuration"), ct);

        Assert.True(response.Headers.Contains(CorrelationIdHeader.HeaderName));
    }

    // Round-2 remediation (ZB-TEN003-01/BIO-TEN-003-F01, Critical): provenance, permission and
    // target-tenant-existence regressions, for both GET and PATCH.

    [Fact]
    public async Task Get_without_a_provenance_header_is_rejected_401()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenantId = NewTenantId();
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/tenants/{tenantId}/feature-configuration");
        request.Headers.Add(ActorResolver.ActorIdHeader, ActorId);

        var response = await _client.SendAsync(request, ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Patch_without_a_provenance_header_is_rejected_401_and_does_not_mutate()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenantId = NewTenantId();
        var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/tenants/{tenantId}/feature-configuration")
        {
            Content = JsonContent.Create(new { version = 0, visitor_enabled = true }, options: TenantFeatureConfigJsonOptions.Strict),
        };
        request.Headers.Add(ActorResolver.ActorIdHeader, ActorId);

        var response = await _client.SendAsync(request, ct);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var getResponse = await _client.SendAsync(AdminRequest(HttpMethod.Get, $"/api/v1/tenants/{tenantId}/feature-configuration"), ct);
        var body = await getResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        Assert.Equal(0, body.GetProperty("version").GetInt32());
    }

    [Fact]
    public async Task Patch_with_a_forged_provenance_proof_is_rejected_401()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenantId = NewTenantId();
        var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/tenants/{tenantId}/feature-configuration")
        {
            Content = JsonContent.Create(new { version = 0, visitor_enabled = true }, options: TenantFeatureConfigJsonOptions.Strict),
        };
        request.Headers.Add(ActorResolver.ActorIdHeader, ActorId);
        request.Headers.Add(ActorProvenanceGate.ProvenanceHeader, "0000000000000000000000000000000000000000000000000000000000000000");

        var response = await _client.SendAsync(request, ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Patch_from_an_actor_without_the_manage_permission_is_forbidden_403_and_does_not_mutate()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenantId = NewTenantId();
        _permissionGateway.Grant = false;

        var response = await _client.SendAsync(
            AdminRequest(HttpMethod.Patch, $"/api/v1/tenants/{tenantId}/feature-configuration", new { version = 0, visitor_enabled = true }), ct);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        _permissionGateway.Grant = true;
        var getResponse = await _client.SendAsync(AdminRequest(HttpMethod.Get, $"/api/v1/tenants/{tenantId}/feature-configuration"), ct);
        var body = await getResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        Assert.Equal(0, body.GetProperty("version").GetInt32());
    }

    [Fact]
    public async Task Get_from_an_actor_without_the_read_permission_is_forbidden_403()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenantId = NewTenantId();
        _permissionGateway.Grant = false;

        var response = await _client.SendAsync(AdminRequest(HttpMethod.Get, $"/api/v1/tenants/{tenantId}/feature-configuration"), ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Patch_for_a_nonexistent_tenant_returns_404_and_creates_nothing()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenantId = NewTenantId();
        _tenantExistenceGateway.MarkNonexistent(tenantId);

        var response = await _client.SendAsync(
            AdminRequest(HttpMethod.Patch, $"/api/v1/tenants/{tenantId}/feature-configuration", new { version = 0, visitor_enabled = true }), ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_for_a_nonexistent_tenant_returns_404_not_a_fabricated_all_disabled_default()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenantId = NewTenantId();
        _tenantExistenceGateway.MarkNonexistent(tenantId);

        var response = await _client.SendAsync(AdminRequest(HttpMethod.Get, $"/api/v1/tenants/{tenantId}/feature-configuration"), ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task An_actor_denied_on_one_tenant_cannot_use_the_same_request_shape_against_a_different_tenant_it_is_not_scoped_to()
    {
        var ct = TestContext.Current.CancellationToken;
        var deniedTenantId = NewTenantId();
        var allowedTenantId = NewTenantId();

        // A tenant-scoped permission gateway is expected to evaluate per (tenantId, actorId) -
        // this fake is global, so this regression documents the CONTRACT (the real
        // PermissionGatewayAdapter resolves grants from BIO-IDN-006's own tenant-scoped
        // PermissionService) rather than re-proving BIO-IDN-006's own tenant isolation here.
        _permissionGateway.Grant = false;
        var deniedResponse = await _client.SendAsync(
            AdminRequest(HttpMethod.Patch, $"/api/v1/tenants/{deniedTenantId}/feature-configuration", new { version = 0, visitor_enabled = true }), ct);
        Assert.Equal(HttpStatusCode.Forbidden, deniedResponse.StatusCode);

        _permissionGateway.Grant = true;
        var allowedResponse = await _client.SendAsync(
            AdminRequest(HttpMethod.Patch, $"/api/v1/tenants/{allowedTenantId}/feature-configuration", new { version = 0, visitor_enabled = true }), ct);
        Assert.Equal(HttpStatusCode.OK, allowedResponse.StatusCode);
    }

    // Round-2 remediation (ZB-TEN003-02/BIO-TEN-003-F02, High): disabling the parent Biometrics
    // module fails closed for Face 1:N even when the child flag remains true.

    [Fact]
    public async Task Disabling_biometrics_after_enabling_both_leaves_face_1n_effectively_disabled()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenantId = NewTenantId();

        var enableBoth = await _client.SendAsync(
            AdminRequest(HttpMethod.Patch, $"/api/v1/tenants/{tenantId}/feature-configuration",
                new { version = 0, biometrics = new { enabled = true, face_1n_identification_enabled = true } }), ct);
        var enabled = await enableBoth.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        Assert.True(enabled.GetProperty("biometrics").GetProperty("enabled").GetBoolean());
        Assert.True(enabled.GetProperty("biometrics").GetProperty("face_1n_identification_enabled").GetBoolean());

        var disableParent = await _client.SendAsync(
            AdminRequest(HttpMethod.Patch, $"/api/v1/tenants/{tenantId}/feature-configuration",
                new { version = 1, biometrics = new { enabled = false } }), ct);
        var disabled = await disableParent.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);

        // The stored child flag is untouched (AC02: independent fields) ...
        Assert.False(disabled.GetProperty("biometrics").GetProperty("enabled").GetBoolean());
        Assert.True(disabled.GetProperty("biometrics").GetProperty("face_1n_identification_enabled").GetBoolean());
    }
}

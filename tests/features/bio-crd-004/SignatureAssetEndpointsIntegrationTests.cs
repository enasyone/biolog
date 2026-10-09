// Developer tests for BIO-CRD-004. SYNTHETIC fixtures only - no real signatures/biometrics.
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Biolog.Features.CardObjects.Api;
using Biolog.Features.CardObjects.Domain;
using Biolog.Features.CardObjects.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Biolog.Features.CardObjects.Tests;

/// <summary>
/// Real HTTP pipeline tests for BIO-CRD-004's signature-asset endpoints, self-hosted via
/// <see cref="TestServer"/>. Wires this project's own DI registrations and endpoint groups'
/// <c>Map</c> methods directly, never through <c>Biolog.Api.Modules.BioCrd004Module</c> - this
/// project has no <c>ProjectReference</c> to <c>Biolog.Api.csproj</c>. Tenant context is a fake
/// here (<see cref="FakeCurrentTenantProvider"/>, mutable, registered Singleton, so a single
/// test can swap tenants mid-flow to prove cross-tenant isolation over real HTTP).
/// </summary>
public sealed class SignatureAssetEndpointsIntegrationTests : IAsyncLifetime
{
    private const string TenantA = "ten_018f2e5a-1234-7abc-89ab-0123456789ab";
    private const string TenantB = "ten_018f2e5a-1234-7abc-89ab-0123456789ac";

    private IHost _host = null!;
    private HttpClient _client = null!;
    private FakeCurrentTenantProvider _tenantProvider = null!;

    public async ValueTask InitializeAsync()
    {
        _tenantProvider = new FakeCurrentTenantProvider(TenantA);

        var builder = new HostBuilder().ConfigureWebHost(webHost =>
        {
            webHost.UseTestServer();
            webHost.ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddSingleton<ICurrentTenantProvider>(_tenantProvider);
                services.AddSingleton<ISignatureAssetRepository, InMemorySignatureAssetRepository>();
                services.AddScoped<SignatureAssetService>();
            });
            webHost.Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(SignatureAssetEndpoints.Map);
            });
        });

        _host = await builder.StartAsync(TestContext.Current.CancellationToken);
        _client = _host.GetTestClient();
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _host.StopAsync(CancellationToken.None);
        _host.Dispose();
    }

    private static string Base64Of(params byte[] bytes) => Convert.ToBase64String(bytes);

    [Fact]
    public async Task AC04_appending_a_signature_starts_at_version_1_and_increments()
    {
        var first = await _client.PostAsJsonAsync(
            "/api/v1/card-designer/signatures/lineage-1/versions",
            new { content_base64 = Base64Of(1, 2, 3) }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var firstBody = await first.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal(1, firstBody.GetProperty("version").GetInt32());

        var second = await _client.PostAsJsonAsync(
            "/api/v1/card-designer/signatures/lineage-1/versions",
            new { content_base64 = Base64Of(4, 5, 6) }, TestContext.Current.CancellationToken);
        var secondBody = await second.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal(2, secondBody.GetProperty("version").GetInt32());
    }

    [Fact]
    public async Task AC04_the_latest_endpoint_returns_the_highest_version_and_a_specific_version_is_still_readable()
    {
        await _client.PostAsJsonAsync("/api/v1/card-designer/signatures/lineage-2/versions", new { content_base64 = Base64Of(1) }, TestContext.Current.CancellationToken);
        await _client.PostAsJsonAsync("/api/v1/card-designer/signatures/lineage-2/versions", new { content_base64 = Base64Of(2) }, TestContext.Current.CancellationToken);

        var latest = await _client.GetFromJsonAsync<JsonElement>("/api/v1/card-designer/signatures/lineage-2/versions/latest", TestContext.Current.CancellationToken);
        Assert.Equal(2, latest.GetProperty("version").GetInt32());

        var version1 = await _client.GetAsync("/api/v1/card-designer/signatures/lineage-2/versions/1", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, version1.StatusCode);
    }

    [Fact]
    public async Task AC04_list_versions_returns_every_appended_version()
    {
        await _client.PostAsJsonAsync("/api/v1/card-designer/signatures/lineage-3/versions", new { content_base64 = Base64Of(1) }, TestContext.Current.CancellationToken);
        await _client.PostAsJsonAsync("/api/v1/card-designer/signatures/lineage-3/versions", new { content_base64 = Base64Of(2) }, TestContext.Current.CancellationToken);
        await _client.PostAsJsonAsync("/api/v1/card-designer/signatures/lineage-3/versions", new { content_base64 = Base64Of(3) }, TestContext.Current.CancellationToken);

        var body = await _client.GetFromJsonAsync<JsonElement>("/api/v1/card-designer/signatures/lineage-3/versions", TestContext.Current.CancellationToken);
        var versions = body.GetProperty("data").EnumerateArray().Select(e => e.GetProperty("version").GetInt32()).ToList();
        Assert.Equal([1, 2, 3], versions);
    }

    [Fact]
    public async Task An_unknown_version_is_404_with_the_signature_version_not_found_code()
    {
        var response = await _client.GetAsync("/api/v1/card-designer/signatures/no-such-lineage/versions/1", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("signature_version_not_found", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task An_empty_content_payload_is_rejected_as_a_400_not_appended()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/v1/card-designer/signatures/lineage-4/versions",
            new { content_base64 = "" }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var latest = await _client.GetAsync("/api/v1/card-designer/signatures/lineage-4/versions/latest", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, latest.StatusCode); // nothing was appended
    }

    [Fact]
    public async Task Non_base64_content_is_rejected_as_a_400_not_appended()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/v1/card-designer/signatures/lineage-5/versions",
            new { content_base64 = "not-valid-base64!!" }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_signature_lineage_created_in_one_tenant_is_invisible_and_unresumable_from_another_tenant()
    {
        // Tenant A appends version 1 for a lineage id.
        _tenantProvider.TenantId = TenantA;
        await _client.PostAsJsonAsync("/api/v1/card-designer/signatures/shared-lineage/versions", new { content_base64 = Base64Of(1) }, TestContext.Current.CancellationToken);

        // Tenant B, using the SAME lineage id, sees nothing from tenant A...
        _tenantProvider.TenantId = TenantB;
        var crossTenantRead = await _client.GetAsync("/api/v1/card-designer/signatures/shared-lineage/versions/latest", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, crossTenantRead.StatusCode);

        // ...and appending under that same lineage id from tenant B starts its OWN sequence at 1,
        // never continuing tenant A's sequence and never overwriting tenant A's row.
        var tenantBAppend = await _client.PostAsJsonAsync("/api/v1/card-designer/signatures/shared-lineage/versions", new { content_base64 = Base64Of(9) }, TestContext.Current.CancellationToken);
        var tenantBBody = await tenantBAppend.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal(1, tenantBBody.GetProperty("version").GetInt32());

        _tenantProvider.TenantId = TenantA;
        var tenantAStillIntact = await _client.GetFromJsonAsync<JsonElement>("/api/v1/card-designer/signatures/shared-lineage/versions/latest", TestContext.Current.CancellationToken);
        Assert.Equal(1, tenantAStillIntact.GetProperty("version").GetInt32());
        Assert.Equal(1, tenantAStillIntact.GetProperty("size_bytes").GetInt32()); // still the 1-byte payload tenant A appended, not tenant B's
    }

    [Fact]
    public async Task No_tenant_context_resolved_fails_closed_with_428_not_a_silent_default()
    {
        _tenantProvider.TenantId = null;
        var response = await _client.PostAsJsonAsync(
            "/api/v1/card-designer/signatures/lineage-6/versions",
            new { content_base64 = Base64Of(1) }, TestContext.Current.CancellationToken);

        Assert.Equal((HttpStatusCode)428, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("tenant_context_missing", body.GetProperty("code").GetString());
    }
}

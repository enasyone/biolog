using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Biolog.Api.Tenancy;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Biolog.IntegrationTests;

/// <summary>
/// Developer tests for BIO-BIO-008-AC05: exercised through the real composed host, the real
/// <see cref="Api.Modules.BioBio008Module"/> DI wiring, and the EXACT endpoint path
/// `web/features/biometrics/live.ts` (BIO-UX-008) already targets for its <c>checkLiveness</c>
/// method. Uses the same header-switched tenant test seam
/// <c>NotificationEndpointsTests</c>/<c>DeviceRegistryTests</c> already established, for the same
/// reason documented there.
/// </summary>
public sealed class LivenessEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private const string TestTenantHeader = "X-Test-Tenant-Id";
    private const string Endpoint = "/api/v1/biometrics/face/liveness";

    public LivenessEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
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
    public async Task AC05_the_exact_web_contract_shape_is_returned_for_the_live_hint()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = CreateClientForTenant(WithTenantSwitching(), "ten_bio008_live");

        var response = await client.PostAsJsonAsync(Endpoint, new { hint = "live" }, ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("live", body.GetProperty("outcome").GetString());
        Assert.True(body.GetProperty("simulated").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("message").GetString()));
    }

    [Fact]
    public async Task AC04_the_spoof_suspected_hint_deterministically_returns_spoof_suspected()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = CreateClientForTenant(WithTenantSwitching(), "ten_bio008_spoof");

        var response = await client.PostAsJsonAsync(Endpoint, new { hint = "spoof_suspected" }, ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("spoof_suspected", body.GetProperty("outcome").GetString());
        Assert.True(body.GetProperty("simulated").GetBoolean());
    }

    [Fact]
    public async Task no_hint_still_returns_the_same_response_shape()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = CreateClientForTenant(WithTenantSwitching(), "ten_bio008_nohint");

        var response = await client.PostAsJsonAsync(Endpoint, new { }, ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.True(body.TryGetProperty("outcome", out _));
        Assert.True(body.TryGetProperty("message", out _));
        Assert.True(body.GetProperty("simulated").GetBoolean());
    }

    [Fact]
    public async Task fails_closed_with_no_resolved_tenant_context()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(Endpoint, new { hint = "live" }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}

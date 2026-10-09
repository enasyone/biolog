using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Biolog.Api.Features.DeviceCertification;
using Biolog.Api.Features.DeviceRegistry;
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
/// Developer tests for BIO-DEV-003, exercised through the real composed host: the real
/// <see cref="Api.Modules.BioDev003Module"/> DI wiring, the real
/// <see cref="Api.Features.DeviceCertification.DeviceCertificationService"/> (not a fake
/// gate - <c>Biolog.Features.DeviceAdapters.Tests.DeviceAdapterServiceTests</c> already
/// covers the orchestration logic in isolation), and the real
/// <see cref="Api.Features.DeviceRegistry.DeviceService"/> device lookup. Uses the same
/// header-switched tenant test seam <c>DeviceRegistryTests</c> established, for the same
/// reason documented there.
/// </summary>
public sealed class DeviceAdapterEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private const string TestTenantHeader = "X-Test-Tenant-Id";

    public DeviceAdapterEndpointsTests(WebApplicationFactory<Program> factory)
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

    private static async Task<string> RegisterDeviceAsync(
        HttpClient client, string vendor, string model, string firmware, string sdk, string serial, CancellationToken ct)
    {
        var request = new DeviceRegistrationRequest(
            serial, vendor, model, firmware, sdk, "Main Campus", new NetworkMetadata(null, null, null), CredentialReference: null);
        var response = await client.PostAsJsonAsync("/api/v1/devices", request, ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<Device>(ct);
        return created!.Id;
    }

    private static async Task RegisterGlobalCertificationAsync(
        HttpClient client, string vendor, string model, string firmware, string sdk, CertificationStatus status, CancellationToken ct)
    {
        var request = new DeviceCertificationRegistrationRequest(
            vendor, model, firmware, sdk, status, "https://evidence.internal/reports/bio-dev-003",
            ActorType: "user", ActorId: "usr_ops", CorrelationId: null);
        var response = await client.PostAsJsonAsync("/api/v1/device-certifications", request, ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task AC02_capability_discovery_and_health_succeed_for_a_certified_device()
    {
        var ct = TestContext.Current.CancellationToken;
        var factory = WithTenantSwitching();
        var client = CreateClientForTenant(factory, "ten_dev003_ac02");
        var controlPlaneClient = factory.CreateClient();

        await RegisterGlobalCertificationAsync(controlPlaneClient, "Acme", "FS-100-AC02", "2.3.1", "sdk-4.0", CertificationStatus.Certified, ct);
        var deviceId = await RegisterDeviceAsync(client, "Acme", "FS-100-AC02", "2.3.1", "sdk-4.0", "SN-AC02-1", ct);

        var capabilitiesResponse = await client.GetAsync($"/api/v1/devices/{deviceId}/capabilities", ct);
        Assert.Equal(HttpStatusCode.OK, capabilitiesResponse.StatusCode);
        var capabilities = await capabilitiesResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("FingerprintScanner", capabilities.GetProperty("category").GetString());

        var healthResponse = await client.GetAsync($"/api/v1/devices/{deviceId}/health", ct);
        Assert.Equal(HttpStatusCode.OK, healthResponse.StatusCode);
        var health = await healthResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("Online", health.GetProperty("status").GetString());
    }

    [Fact]
    public async Task AC03_a_combination_with_no_certification_record_is_rejected_with_the_certification_status()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = CreateClientForTenant(WithTenantSwitching(), "ten_dev003_ac03_norecord");

        // Deliberately never registers a certification entry for this combination.
        var deviceId = await RegisterDeviceAsync(client, "Acme", "FS-999-UNSEEN", "9.9.9", "sdk-9", "SN-AC03-1", ct);

        var response = await client.GetAsync($"/api/v1/devices/{deviceId}/capabilities", ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("device_combination_unsupported", body.GetProperty("title").GetString());
        Assert.Equal("NoCertificationRecord", body.GetProperty("certification_status").GetString());
    }

    [Fact]
    public async Task AC03_a_combination_explicitly_marked_unsupported_is_rejected_with_that_exact_status()
    {
        var ct = TestContext.Current.CancellationToken;
        var factory = WithTenantSwitching();
        var client = CreateClientForTenant(factory, "ten_dev003_ac03_unsupported");
        var controlPlaneClient = factory.CreateClient();

        await RegisterGlobalCertificationAsync(
            controlPlaneClient, "Acme", "FS-LEGACY-AC03", "0.1.0", "sdk-0", CertificationStatus.Unsupported, ct);
        var deviceId = await RegisterDeviceAsync(client, "Acme", "FS-LEGACY-AC03", "0.1.0", "sdk-0", "SN-AC03-2", ct);

        var response = await client.GetAsync($"/api/v1/devices/{deviceId}/health", ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("Unsupported", body.GetProperty("certification_status").GetString());
    }

    [Fact]
    public async Task a_certified_combination_with_no_registered_adapter_reports_adapter_unavailable_not_a_generic_error()
    {
        var ct = TestContext.Current.CancellationToken;
        var factory = WithTenantSwitching();
        var client = CreateClientForTenant(factory, "ten_dev003_noadapter");
        var controlPlaneClient = factory.CreateClient();

        await RegisterGlobalCertificationAsync(
            controlPlaneClient, "UnknownVendor", "X-1", "1.0.0", "sdk-1", CertificationStatus.Certified, ct);
        var deviceId = await RegisterDeviceAsync(client, "UnknownVendor", "X-1", "1.0.0", "sdk-1", "SN-NA-1", ct);

        var response = await client.GetAsync($"/api/v1/devices/{deviceId}/capabilities", ct);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("device_adapter_unavailable", body.GetProperty("title").GetString());
    }

    [Fact]
    public async Task capabilities_endpoint_fails_closed_with_no_resolved_tenant_context()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(
            "/api/v1/devices/dev_00000000-0000-7000-8000-000000000000/capabilities", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task capabilities_endpoint_returns_not_found_for_an_unknown_device_in_a_resolved_tenant()
    {
        using var client = CreateClientForTenant(WithTenantSwitching(), "ten_dev003_notfound");

        var response = await client.GetAsync(
            "/api/v1/devices/dev_00000000-0000-7000-8000-000000000000/capabilities", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}

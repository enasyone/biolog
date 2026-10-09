using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Biolog.Api.Audit;
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

/// <summary>Developer tests for BIO-DEV-001, exercised through the real composed host
/// (module wiring, routing, JSON serialization) rather than unit-level assertions on
/// DeviceService alone.</summary>
public sealed class DeviceRegistryTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public DeviceRegistryTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
    }

    private static DeviceRegistrationRequest SampleRequest() => new(
        "SN-9001", "Acme", "ReaderX", "1.0.0", "sdk-1", "Main Campus",
        new NetworkMetadata("10.0.0.9", null, "reader-09"), CredentialReference: "kv://synthetic/reader-09");

    private const string TestTenantHeader = "X-Test-Tenant-Id";

    /// <summary>
    /// Regression for BIO-DEV-001-QA-01: every prior integration test asserted only the
    /// fail-closed path, which is correct given BIO-FND-008's tenant hook resolves nothing
    /// today - but proved nothing about whether this feature's OWN logic works at all
    /// through the real host. This gives a resolved tenant to a request WITHOUT changing
    /// production <c>TenantContextMiddleware</c> or <c>TenantContextAccessor</c> in any way:
    /// it swaps in a test-only <see cref="ITenantContextAccessor"/> whose setter ignores the
    /// middleware's unconditional <c>Context = null</c>, resolving instead from a
    /// per-request test header. <see cref="RemoveAll{T}"/> then <c>AddScoped</c> ensures
    /// this replaces the production registration outright rather than merely shadowing it.
    ///
    /// This is ONE shared factory (and so one shared <see cref="InMemoryDeviceRepository"/>
    /// singleton) that every tenant switches through via the header, rather than a separate
    /// factory per tenant - a cross-tenant isolation test needs both tenants to actually
    /// share the same underlying store to prove anything; two independent factories would
    /// each get their own empty repository and "pass" for a reason that has nothing to do
    /// with tenant isolation.
    ///
    /// Every other test in this file uses the unmodified <see cref="_factory"/> and still
    /// proves the real, current, correct production behaviour: an unresolved tenant fails
    /// closed.
    /// </summary>
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
            set { /* ignore - resolved from the test header instead, immune to the middleware's null-set */ }
        }
    }

    [Fact]
    public async Task AC04_registering_a_device_fails_closed_with_no_resolved_tenant_context()
    {
        // The true, current, correct behaviour of the full pipeline (ADR-0002 Decision 2):
        // BIO-FND-008's tenant hook resolves no tenant yet, so every device endpoint must
        // fail closed rather than proceed with an inferred or default tenant. This is not
        // a defect to work around in the test - it is the thing AC04 requires proving.
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/devices", SampleRequest(), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("tenant_context_missing", body.GetProperty("title").GetString());
    }

    [Fact]
    public async Task AC04_reading_a_device_fails_closed_with_no_resolved_tenant_context()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(
            "/api/v1/devices/dev_00000000-0000-7000-8000-000000000000", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AC03_changing_device_status_fails_closed_with_no_resolved_tenant_context()
    {
        using var client = _factory.CreateClient();
        var request = new DeviceStatusChangeRequest(DeviceStatus.Active, 1, "user", "usr_admin", null);

        var response = await client.PatchAsJsonAsync(
            "/api/v1/devices/dev_00000000-0000-7000-8000-000000000000/status", request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task devices_endpoints_are_routed_under_the_versioned_api_path()
    {
        // ADR-0004 §1.1: every public HTTP endpoint is versioned in its path.
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/devices/dev_00000000-0000-7000-8000-000000000000", TestContext.Current.CancellationToken);

        // Routed (400 tenant_context_missing), not a 404 from an unmatched route - proves
        // the endpoint exists at this path rather than merely not colliding with one.
        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AC01_registering_and_reading_back_a_device_succeeds_with_a_resolved_tenant()
    {
        using var client = CreateClientForTenant(WithTenantSwitching(), "ten_asu");

        var createResponse = await client.PostAsJsonAsync(
            "/api/v1/devices", SampleRequest(), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<Device>(TestContext.Current.CancellationToken);

        Assert.NotNull(created);
        Assert.StartsWith("dev_", created!.Id);
        Assert.Equal("ten_asu", created.TenantId);
        Assert.Equal("SN-9001", created.Serial);
        Assert.Equal("Acme", created.Vendor);
        Assert.Equal("ReaderX", created.Model);
        Assert.Equal("1.0.0", created.Firmware);
        Assert.Equal("sdk-1", created.Sdk);
        Assert.Equal("Main Campus", created.Site);
        Assert.Equal("10.0.0.9", created.Network.IpAddress);
        Assert.Equal(DeviceStatus.PendingActivation, created.Status);
        Assert.Null(created.LastSeenAt);
        Assert.Equal("kv://synthetic/reader-09", created.CredentialReference);

        var getResponse = await client.GetAsync($"/api/v1/devices/{created.Id}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var fetched = await getResponse.Content.ReadFromJsonAsync<Device>(TestContext.Current.CancellationToken);
        Assert.Equal(created.Id, fetched!.Id);
    }

    [Fact]
    public async Task AC03_changing_status_with_a_resolved_tenant_records_an_audit_event()
    {
        var factory = WithTenantSwitching();
        using var client = CreateClientForTenant(factory, "ten_asu");

        var createResponse = await client.PostAsJsonAsync(
            "/api/v1/devices", SampleRequest(), TestContext.Current.CancellationToken);
        var created = await createResponse.Content.ReadFromJsonAsync<Device>(TestContext.Current.CancellationToken);

        var statusRequest = new DeviceStatusChangeRequest(DeviceStatus.Active, created!.Version, "user", "usr_admin", null);
        var statusResponse = await client.PatchAsJsonAsync(
            $"/api/v1/devices/{created.Id}/status", statusRequest, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, statusResponse.StatusCode);
        var updated = await statusResponse.Content.ReadFromJsonAsync<Device>(TestContext.Current.CancellationToken);
        Assert.Equal(DeviceStatus.Active, updated!.Status);

        using var scope = factory.Services.CreateScope();
        var auditSink = (InMemoryAuditSink)scope.ServiceProvider.GetRequiredService<IAuditSink>();
        var recorded = Assert.Single(auditSink.Events, e => e.Target.Id == created.Id);
        Assert.Equal("device.status_changed", recorded.Action);
        Assert.Equal("ten_asu", recorded.TenantId);
    }

    [Fact]
    public async Task AC04_a_device_registered_under_one_resolved_tenant_is_not_visible_to_a_different_resolved_tenant()
    {
        var factory = WithTenantSwitching();
        using var clientA = CreateClientForTenant(factory, "ten_asu");
        using var clientB = CreateClientForTenant(factory, "ten_grace");

        var createResponse = await clientA.PostAsJsonAsync(
            "/api/v1/devices", SampleRequest(), TestContext.Current.CancellationToken);
        var created = await createResponse.Content.ReadFromJsonAsync<Device>(TestContext.Current.CancellationToken);

        var crossTenantGet = await clientB.GetAsync($"/api/v1/devices/{created!.Id}", TestContext.Current.CancellationToken);
        var sameTenantGet = await clientA.GetAsync($"/api/v1/devices/{created.Id}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, crossTenantGet.StatusCode); // not forbidden - ADR-0002 §4.3
        Assert.Equal(HttpStatusCode.OK, sameTenantGet.StatusCode);
    }

    [Fact]
    public async Task QA04_registering_a_device_with_a_plaintext_credential_reference_is_rejected_by_the_composed_host()
    {
        // BIO-DEV-001-QA-04 (Chioma, acceptance): the acceptance probe submitted
        // "synthetic-plaintext-password" through this exact shape and it was accepted,
        // stored, and returned unchanged. Through the real composed host, this must now be
        // a 400, and the device must never exist afterwards.
        using var client = CreateClientForTenant(WithTenantSwitching(), "ten_asu");
        var request = SampleRequest() with { CredentialReference = "synthetic-plaintext-password" };

        var response = await client.PostAsJsonAsync("/api/v1/devices", request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("invalid_credential_reference", body.GetProperty("title").GetString());
    }

    [Fact]
    public async Task QA04_registering_a_device_with_a_supported_credential_reference_still_succeeds()
    {
        // The fix for QA-04 must reject plaintext without also breaking the legitimate path.
        using var client = CreateClientForTenant(WithTenantSwitching(), "ten_asu");
        var request = SampleRequest() with { CredentialReference = "vault://devices/reader-09/cred" };

        var response = await client.PostAsJsonAsync("/api/v1/devices", request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<Device>(TestContext.Current.CancellationToken);
        Assert.Equal("vault://devices/reader-09/cred", created!.CredentialReference);
    }
}
